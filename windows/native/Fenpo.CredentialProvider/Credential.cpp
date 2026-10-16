#include "Credential.h"
#include "Provider.h"
#include "UnlockPipe.h"
#include "resource.h"

#pragma comment(lib, "secur32.lib")

extern HINSTANCE g_instance;

namespace
{
    void InitString(UNICODE_STRING& target, const std::wstring& text)
    {
        target.Length = static_cast<USHORT>(text.size() * sizeof(wchar_t));
        target.MaximumLength = target.Length;
        target.Buffer = const_cast<PWSTR>(text.c_str());
    }

    // Copies a string after the structure and stores its offset, as LSA expects a packed KERB_INTERACTIVE_UNLOCK_LOGON.
    void PackString(const UNICODE_STRING& source, BYTE* base, BYTE*& next, UNICODE_STRING& target)
    {
        target.Length = source.Length;
        target.MaximumLength = source.Length;
        CopyMemory(next, source.Buffer, source.Length);
        target.Buffer = reinterpret_cast<PWSTR>(next - base);
        next += source.Length;
    }

    HRESULT Serialize(const std::wstring& domain, const std::wstring& user, const std::wstring& password, BYTE** buffer, DWORD* size)
    {
        KERB_INTERACTIVE_UNLOCK_LOGON logon = {};
        logon.Logon.MessageType = KerbWorkstationUnlockLogon;
        InitString(logon.Logon.LogonDomainName, domain);
        InitString(logon.Logon.UserName, user);
        InitString(logon.Logon.Password, password);

        const DWORD total = sizeof(logon) + logon.Logon.LogonDomainName.Length + logon.Logon.UserName.Length + logon.Logon.Password.Length;
        auto* packed = static_cast<BYTE*>(CoTaskMemAlloc(total));

        if (!packed)
        {
            return E_OUTOFMEMORY;
        }

        ZeroMemory(packed, total);
        auto* result = reinterpret_cast<KERB_INTERACTIVE_UNLOCK_LOGON*>(packed);
        result->Logon.MessageType = logon.Logon.MessageType;
        BYTE* next = packed + sizeof(logon);
        PackString(logon.Logon.LogonDomainName, packed, next, result->Logon.LogonDomainName);
        PackString(logon.Logon.UserName, packed, next, result->Logon.UserName);
        PackString(logon.Logon.Password, packed, next, result->Logon.Password);
        *buffer = packed;
        *size = total;
        return S_OK;
    }

    HRESULT NegotiatePackage(ULONG* package)
    {
        HANDLE lsa = nullptr;

        if (LsaConnectUntrusted(&lsa) != 0)
        {
            return E_FAIL;
        }

        LSA_STRING name;
        char negotiate[] = "Negotiate";
        name.Buffer = negotiate;
        name.Length = static_cast<USHORT>(strlen(negotiate));
        name.MaximumLength = name.Length + 1;
        const NTSTATUS status = LsaLookupAuthenticationPackage(lsa, &name, package);
        LsaDeregisterLogonProcess(lsa);
        return status == 0 ? S_OK : E_FAIL;
    }

    UINT StatusText(const std::wstring& reason)
    {
        if (reason == L"declined") return IDS_DECLINED;
        if (reason == L"notconnected") return IDS_NOT_CONNECTED;
        if (reason == L"notpaired") return IDS_NOT_PAIRED;
        if (reason == L"timeout") return IDS_TIMEOUT;
        return IDS_FAILED;
    }
}

Credential::Credential(Provider* provider, ICredentialProviderUser* user, const std::wstring& sid)
    : _provider(provider), _user(user), _sid(sid)
{
    InitializeCriticalSection(&_lock);
    _user->AddRef();
    _status = LoadText(IDS_IDLE);

    PWSTR qualified = nullptr;

    // "MicrosoftAccount\name@example.com" or "PC\name": what LogonUI itself uses for this user.
    if (SUCCEEDED(_user->GetStringValue(PKEY_Identity_QualifiedUserName, &qualified)))
    {
        _qualifiedUser = qualified;
        CoTaskMemFree(qualified);
    }

    DllAddRef();
}

Credential::~Credential()
{
    if (_thread)
    {
        WaitForSingleObject(_thread, 2000);
        CloseHandle(_thread);
    }

    ClearPassword();
    _user->Release();
    DeleteCriticalSection(&_lock);
    DllRelease();
}

IFACEMETHODIMP_(ULONG) Credential::AddRef()
{
    return InterlockedIncrement(&_references);
}

IFACEMETHODIMP_(ULONG) Credential::Release()
{
    const LONG references = InterlockedDecrement(&_references);

    if (references == 0)
    {
        delete this;
    }

    return references;
}

IFACEMETHODIMP Credential::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB table[] = {
        QITABENT(Credential, ICredentialProviderCredential),
        QITABENT(Credential, ICredentialProviderCredential2),
        { nullptr, 0 },
    };
    return QISearch(this, table, riid, ppv);
}

IFACEMETHODIMP Credential::Advise(ICredentialProviderCredentialEvents* events)
{
    EnterCriticalSection(&_lock);

    if (_events)
    {
        _events->Release();
    }

    _events = events;
    _events->AddRef();
    LeaveCriticalSection(&_lock);
    return S_OK;
}

IFACEMETHODIMP Credential::UnAdvise()
{
    EnterCriticalSection(&_lock);

    if (_events)
    {
        _events->Release();
        _events = nullptr;
    }

    LeaveCriticalSection(&_lock);
    return S_OK;
}

IFACEMETHODIMP Credential::SetSelected(BOOL* autoLogon)
{
    // Once the password is here LogonUI logs on by itself; until then picking the tile asks the phone.
    *autoLogon = Ready() ? TRUE : FALSE;

    if (!Ready())
    {
        StartAsking();
    }

    return S_OK;
}

IFACEMETHODIMP Credential::SetDeselected()
{
    // Another tile was picked: stop waiting for the phone, and forget any password.
    EnterCriticalSection(&_lock);
    HANDLE pipe = _pipe;

    if (pipe)
    {
        CancelIoEx(pipe, nullptr);
    }

    LeaveCriticalSection(&_lock);
    ClearPassword();
    InterlockedExchange(&_ready, 0);
    return S_OK;
}

IFACEMETHODIMP Credential::GetFieldState(DWORD field, CREDENTIAL_PROVIDER_FIELD_STATE* state, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* interactive)
{
    if (field >= FieldCount)
    {
        return E_INVALIDARG;
    }

    *interactive = CPFIS_NONE;
    *state = field == FieldTileImage || field == FieldTitle ? CPFS_DISPLAY_IN_BOTH : CPFS_DISPLAY_IN_SELECTED_TILE;
    return S_OK;
}

IFACEMETHODIMP Credential::GetStringValue(DWORD field, PWSTR* value)
{
    *value = nullptr;

    switch (field)
    {
    case FieldTitle:
        return DuplicateString(LoadText(IDS_TITLE), value);
    case FieldRetry:
        return DuplicateString(LoadText(IDS_RETRY), value);
    case FieldStatus:
    {
        EnterCriticalSection(&_lock);
        const std::wstring status = _status;
        LeaveCriticalSection(&_lock);
        return DuplicateString(status, value);
    }
    default:
        return E_INVALIDARG;
    }
}

IFACEMETHODIMP Credential::GetBitmapValue(DWORD field, HBITMAP* bitmap)
{
    *bitmap = nullptr;

    if (field != FieldTileImage)
    {
        return E_INVALIDARG;
    }

    *bitmap = static_cast<HBITMAP>(LoadImageW(g_instance, MAKEINTRESOURCEW(IDB_TILE), IMAGE_BITMAP, 0, 0, LR_CREATEDIBSECTION));
    return *bitmap ? S_OK : HRESULT_FROM_WIN32(GetLastError());
}

IFACEMETHODIMP Credential::GetCheckboxValue(DWORD, BOOL*, PWSTR*)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::GetSubmitButtonValue(DWORD, DWORD*)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::GetComboBoxValueCount(DWORD, DWORD*, DWORD*)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::GetComboBoxValueAt(DWORD, DWORD, PWSTR*)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::SetStringValue(DWORD, PCWSTR)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::SetCheckboxValue(DWORD, BOOL)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::SetComboBoxSelectedValue(DWORD, DWORD)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Credential::CommandLinkClicked(DWORD field)
{
    if (field != FieldRetry)
    {
        return E_INVALIDARG;
    }

    StartAsking();
    return S_OK;
}

IFACEMETHODIMP Credential::GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* response, CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization, PWSTR* statusText, CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon)
{
    *response = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
    *statusText = nullptr;
    *statusIcon = CPSI_NONE;
    ZeroMemory(serialization, sizeof(*serialization));

    EnterCriticalSection(&_lock);
    const std::wstring password = _password;
    LeaveCriticalSection(&_lock);

    const size_t slash = _qualifiedUser.find(L'\\');

    if (!Ready() || password.empty() || slash == std::wstring::npos)
    {
        return S_OK;
    }

    HRESULT hr = Serialize(_qualifiedUser.substr(0, slash), _qualifiedUser.substr(slash + 1), password, &serialization->rgbSerialization, &serialization->cbSerialization);

    if (SUCCEEDED(hr))
    {
        hr = NegotiatePackage(&serialization->ulAuthenticationPackage);
    }

    if (SUCCEEDED(hr))
    {
        serialization->clsidCredentialProvider = CLSID_FenpoCredentialProvider;
        *response = CPGSR_RETURN_CREDENTIAL_FINISHED;
    }
    else if (serialization->rgbSerialization)
    {
        SecureZeroMemory(serialization->rgbSerialization, serialization->cbSerialization);
        CoTaskMemFree(serialization->rgbSerialization);
        serialization->rgbSerialization = nullptr;
    }

    // Used once: a failed logon (a changed Windows password) needs a new answer from the phone.
    ClearPassword();
    InterlockedExchange(&_ready, 0);
    return hr;
}

IFACEMETHODIMP Credential::ReportResult(NTSTATUS status, NTSTATUS, PWSTR* statusText, CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon)
{
    *statusText = nullptr;
    *statusIcon = CPSI_NONE;

    // Wrong password: the Windows password changed since Fenpo was set up.
    if (status != 0)
    {
        SetStatus(IDS_NOT_PAIRED);
    }

    return S_OK;
}

IFACEMETHODIMP Credential::GetUserSid(PWSTR* sid)
{
    return DuplicateString(_sid, sid);
}

bool Credential::Ready() const
{
    return _ready != 0;
}

void Credential::StartAsking()
{
    if (InterlockedCompareExchange(&_asking, 1, 0) != 0)
    {
        return;
    }

    if (_thread)
    {
        WaitForSingleObject(_thread, INFINITE);
        CloseHandle(_thread);
        _thread = nullptr;
    }

    SetStatus(IDS_WAITING);
    AddRef();
    _thread = CreateThread(nullptr, 0, AskThread, this, 0, nullptr);

    if (!_thread)
    {
        InterlockedExchange(&_asking, 0);
        SetStatus(IDS_FAILED);
        Release();
    }
}

DWORD WINAPI Credential::AskThread(void* parameter)
{
    auto* credential = static_cast<Credential*>(parameter);
    credential->Ask();
    credential->Release();
    return 0;
}

void Credential::Ask()
{
    std::wstring password;
    std::wstring reason;
    const bool ok = UnlockPipe::Unlock(password, reason, &_pipe);

    EnterCriticalSection(&_lock);
    _pipe = nullptr;

    if (ok)
    {
        _password = password;
        _status = LoadText(IDS_READY);
    }
    else
    {
        _status = LoadText(StatusText(reason));
    }

    LeaveCriticalSection(&_lock);
    SecureZeroMemory(password.data(), password.size() * sizeof(wchar_t));
    InterlockedExchange(&_ready, ok ? 1 : 0);
    InterlockedExchange(&_asking, 0);

    // LogonUI reads the credentials again: with the password ready it logs on, otherwise it shows why not.
    _provider->CredentialsChanged();
}

void Credential::SetStatus(UINT text)
{
    EnterCriticalSection(&_lock);
    _status = LoadText(text);
    ICredentialProviderCredentialEvents* events = _events;

    if (events)
    {
        events->AddRef();
    }

    const std::wstring status = _status;
    LeaveCriticalSection(&_lock);

    if (events)
    {
        events->SetFieldString(this, FieldStatus, status.c_str());
        events->Release();
    }
}

void Credential::ClearPassword()
{
    EnterCriticalSection(&_lock);
    SecureZeroMemory(_password.data(), _password.size() * sizeof(wchar_t));
    _password.clear();
    LeaveCriticalSection(&_lock);
}

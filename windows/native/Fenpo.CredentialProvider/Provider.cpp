#include "Provider.h"
#include "Credential.h"
#include "UnlockPipe.h"
#include "resource.h"

namespace
{
    // GUID_CPFG_CREDENTIAL_PROVIDER_LOGO tells LogonUI to use the tile image as the "sign-in options" icon.
    constexpr GUID LogoGuid = { 0x2d837775, 0xf6cd, 0x464e, { 0xa7, 0x45, 0x48, 0x2f, 0xd0, 0xb4, 0x74, 0x93 } };
    constexpr GUID LabelGuid = { 0x286bbff3, 0xbad4, 0x438f, { 0xb0, 0x07, 0x79, 0xb7, 0x26, 0x7c, 0x3d, 0x48 } };

    struct FieldDescription
    {
        CREDENTIAL_PROVIDER_FIELD_TYPE type;
        UINT label;
        GUID guid;
    };

    const FieldDescription Fields[FieldCount] = {
        { CPFT_TILE_IMAGE, IDS_TITLE, LogoGuid },
        { CPFT_LARGE_TEXT, IDS_TITLE, LabelGuid },
        { CPFT_SMALL_TEXT, IDS_IDLE, GUID_NULL },
        { CPFT_COMMAND_LINK, IDS_RETRY, GUID_NULL },
    };
}

Provider::Provider()
{
    InitializeCriticalSection(&_lock);
    DllAddRef();
}

Provider::~Provider()
{
    ReleaseCredential();

    if (_users)
    {
        _users->Release();
    }

    DeleteCriticalSection(&_lock);
    DllRelease();
}

IFACEMETHODIMP_(ULONG) Provider::AddRef()
{
    return InterlockedIncrement(&_references);
}

IFACEMETHODIMP_(ULONG) Provider::Release()
{
    const LONG references = InterlockedDecrement(&_references);

    if (references == 0)
    {
        delete this;
    }

    return references;
}

IFACEMETHODIMP Provider::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB table[] = {
        QITABENT(Provider, ICredentialProvider),
        QITABENT(Provider, ICredentialProviderSetUserArray),
        { nullptr, 0 },
    };
    return QISearch(this, table, riid, ppv);
}

IFACEMETHODIMP Provider::SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO scenario, DWORD)
{
    // Only unlocking: the first sign-in after a restart has no Fenpo running to ask the phone.
    if (scenario != CPUS_UNLOCK_WORKSTATION)
    {
        return E_NOTIMPL;
    }

    _scenario = scenario;
    return S_OK;
}

IFACEMETHODIMP Provider::SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION*)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP Provider::Advise(ICredentialProviderEvents* events, UINT_PTR context)
{
    EnterCriticalSection(&_lock);

    if (_events)
    {
        _events->Release();
    }

    _events = events;
    _events->AddRef();
    _context = context;
    LeaveCriticalSection(&_lock);
    return S_OK;
}

IFACEMETHODIMP Provider::UnAdvise()
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

IFACEMETHODIMP Provider::GetFieldDescriptorCount(DWORD* count)
{
    *count = FieldCount;
    return S_OK;
}

IFACEMETHODIMP Provider::GetFieldDescriptorAt(DWORD index, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** descriptor)
{
    *descriptor = nullptr;

    if (index >= FieldCount)
    {
        return E_INVALIDARG;
    }

    auto* result = static_cast<CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*>(CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR)));

    if (!result)
    {
        return E_OUTOFMEMORY;
    }

    result->dwFieldID = index;
    result->cpft = Fields[index].type;
    result->guidFieldType = Fields[index].guid;
    const HRESULT hr = DuplicateString(LoadText(Fields[index].label), &result->pszLabel);

    if (FAILED(hr))
    {
        CoTaskMemFree(result);
        return hr;
    }

    *descriptor = result;
    return S_OK;
}

IFACEMETHODIMP Provider::GetCredentialCount(DWORD* count, DWORD* defaultIndex, BOOL* autoLogonWithDefault)
{
    EnterCriticalSection(&_lock);

    if (!_credential)
    {
        CreateCredential();
    }

    *count = _credential ? 1 : 0;
    *defaultIndex = CREDENTIAL_PROVIDER_NO_DEFAULT;
    *autoLogonWithDefault = FALSE;

    // The phone approved: log on with the password right away, without another click.
    if (_credential && _credential->Ready())
    {
        *defaultIndex = 0;
        *autoLogonWithDefault = TRUE;
    }

    LeaveCriticalSection(&_lock);
    return S_OK;
}

IFACEMETHODIMP Provider::GetCredentialAt(DWORD index, ICredentialProviderCredential** credential)
{
    *credential = nullptr;
    EnterCriticalSection(&_lock);
    HRESULT hr = E_INVALIDARG;

    if (index == 0 && _credential)
    {
        hr = _credential->QueryInterface(IID_PPV_ARGS(credential));
    }

    LeaveCriticalSection(&_lock);
    return hr;
}

IFACEMETHODIMP Provider::SetUserArray(ICredentialProviderUserArray* users)
{
    EnterCriticalSection(&_lock);

    if (_users)
    {
        _users->Release();
    }

    _users = users;
    _users->AddRef();
    ReleaseCredential();
    LeaveCriticalSection(&_lock);
    return S_OK;
}

void Provider::CredentialsChanged()
{
    EnterCriticalSection(&_lock);
    ICredentialProviderEvents* events = _events;
    const UINT_PTR context = _context;

    if (events)
    {
        events->AddRef();
    }

    LeaveCriticalSection(&_lock);

    if (events)
    {
        events->CredentialsChanged(context);
        events->Release();
    }
}

// A tile only under the user Fenpo is set up for, and only while Fenpo in this session answers.
void Provider::CreateCredential()
{
    if (!_users || _scenario != CPUS_UNLOCK_WORKSTATION)
    {
        return;
    }

    const std::wstring enrolled = UnlockPipe::EnrolledSid();

    if (enrolled.empty())
    {
        return;
    }

    DWORD count = 0;
    _users->GetCount(&count);

    for (DWORD index = 0; index < count && !_credential; index++)
    {
        ICredentialProviderUser* user = nullptr;

        if (FAILED(_users->GetAt(index, &user)))
        {
            continue;
        }

        PWSTR sid = nullptr;

        if (SUCCEEDED(user->GetSid(&sid)) && _wcsicmp(sid, enrolled.c_str()) == 0)
        {
            _credential = new (std::nothrow) Credential(this, user, sid);
        }

        CoTaskMemFree(sid);
        user->Release();
    }
}

void Provider::ReleaseCredential()
{
    if (_credential)
    {
        _credential->Release();
        _credential = nullptr;
    }
}

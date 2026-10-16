#include "Common.h"
#include "Provider.h"
#include <new>

HINSTANCE g_instance = nullptr;

namespace
{
    LONG g_references = 0;

    constexpr wchar_t ProvidersKey[] = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\";
    constexpr wchar_t ClassesKey[] = L"SOFTWARE\\Classes\\CLSID\\";

    class ClassFactory final : public IClassFactory
    {
    public:
        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_references); }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const LONG references = InterlockedDecrement(&_references);

            if (references == 0)
            {
                delete this;
            }

            return references;
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            static const QITAB table[] = { QITABENT(ClassFactory, IClassFactory), { nullptr, 0 } };
            return QISearch(this, table, riid, ppv);
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
        {
            *ppv = nullptr;

            if (outer)
            {
                return CLASS_E_NOAGGREGATION;
            }

            auto* provider = new (std::nothrow) Provider();

            if (!provider)
            {
                return E_OUTOFMEMORY;
            }

            const HRESULT hr = provider->QueryInterface(riid, ppv);
            provider->Release();
            return hr;
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            lock ? DllAddRef() : DllRelease();
            return S_OK;
        }

    private:
        LONG _references = 1;
    };

    HRESULT SetValue(HKEY root, const std::wstring& path, const wchar_t* name, const std::wstring& value)
    {
        HKEY key = nullptr;
        LSTATUS status = RegCreateKeyExW(root, path.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);

        if (status == ERROR_SUCCESS)
        {
            status = RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
            RegCloseKey(key);
        }

        return HRESULT_FROM_WIN32(status);
    }
}

void DllAddRef()
{
    InterlockedIncrement(&g_references);
}

void DllRelease()
{
    InterlockedDecrement(&g_references);
}

std::wstring LoadText(UINT id)
{
    const wchar_t* text = nullptr;
    const int length = LoadStringW(g_instance, id, reinterpret_cast<LPWSTR>(&text), 0);
    return length > 0 ? std::wstring(text, length) : std::wstring();
}

HRESULT DuplicateString(const std::wstring& text, PWSTR* result)
{
    const size_t bytes = (text.size() + 1) * sizeof(wchar_t);
    *result = static_cast<PWSTR>(CoTaskMemAlloc(bytes));

    if (!*result)
    {
        return E_OUTOFMEMORY;
    }

    CopyMemory(*result, text.c_str(), bytes);
    return S_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_instance = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return g_references > 0 ? S_FALSE : S_OK;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    *ppv = nullptr;

    if (clsid != CLSID_FenpoCredentialProvider)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory();

    if (!factory)
    {
        return E_OUTOFMEMORY;
    }

    const HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

// regsvr32, run by Fenpo's administrator-approved install: the COM class with this DLL's path, and the provider in
// LogonUI's list.
STDAPI DllRegisterServer()
{
    wchar_t path[MAX_PATH] = {};

    if (!GetModuleFileNameW(g_instance, path, MAX_PATH))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    const std::wstring classKey = std::wstring(ClassesKey) + ClassIdText;
    HRESULT hr = SetValue(HKEY_LOCAL_MACHINE, classKey, nullptr, L"Fenpo");

    if (SUCCEEDED(hr))
    {
        hr = SetValue(HKEY_LOCAL_MACHINE, classKey + L"\\InprocServer32", nullptr, path);
    }

    if (SUCCEEDED(hr))
    {
        hr = SetValue(HKEY_LOCAL_MACHINE, classKey + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    }

    if (SUCCEEDED(hr))
    {
        hr = SetValue(HKEY_LOCAL_MACHINE, std::wstring(ProvidersKey) + ClassIdText, nullptr, L"Fenpo");
    }

    return hr;
}

STDAPI DllUnregisterServer()
{
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, (std::wstring(ProvidersKey) + ClassIdText).c_str());
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, (std::wstring(ClassesKey) + ClassIdText).c_str());
    return S_OK;
}

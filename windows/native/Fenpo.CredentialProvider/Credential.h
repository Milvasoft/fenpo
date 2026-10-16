#pragma once

#include "Common.h"

class Provider;

// The "Unlock with Fenpo" tile under the user Fenpo was set up for. Selecting it asks the phone (on a worker thread);
// when the password comes back the provider asks LogonUI to log on with it automatically.
class Credential final : public ICredentialProviderCredential2
{
public:
    Credential(Provider* provider, ICredentialProviderUser* user, const std::wstring& sid);

    // IUnknown
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;

    // ICredentialProviderCredential
    IFACEMETHODIMP Advise(ICredentialProviderCredentialEvents* events) override;
    IFACEMETHODIMP UnAdvise() override;
    IFACEMETHODIMP SetSelected(BOOL* autoLogon) override;
    IFACEMETHODIMP SetDeselected() override;
    IFACEMETHODIMP GetFieldState(DWORD field, CREDENTIAL_PROVIDER_FIELD_STATE* state, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* interactive) override;
    IFACEMETHODIMP GetStringValue(DWORD field, PWSTR* value) override;
    IFACEMETHODIMP GetBitmapValue(DWORD field, HBITMAP* bitmap) override;
    IFACEMETHODIMP GetCheckboxValue(DWORD field, BOOL* checked, PWSTR* label) override;
    IFACEMETHODIMP GetSubmitButtonValue(DWORD field, DWORD* adjacentTo) override;
    IFACEMETHODIMP GetComboBoxValueCount(DWORD field, DWORD* count, DWORD* selected) override;
    IFACEMETHODIMP GetComboBoxValueAt(DWORD field, DWORD item, PWSTR* value) override;
    IFACEMETHODIMP SetStringValue(DWORD field, PCWSTR value) override;
    IFACEMETHODIMP SetCheckboxValue(DWORD field, BOOL checked) override;
    IFACEMETHODIMP SetComboBoxSelectedValue(DWORD field, DWORD selected) override;
    IFACEMETHODIMP CommandLinkClicked(DWORD field) override;
    IFACEMETHODIMP GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* response, CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization, PWSTR* statusText, CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon) override;
    IFACEMETHODIMP ReportResult(NTSTATUS status, NTSTATUS subStatus, PWSTR* statusText, CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon) override;

    // ICredentialProviderCredential2
    IFACEMETHODIMP GetUserSid(PWSTR* sid) override;

    // The password is ready: LogonUI may log on with it now.
    bool Ready() const;

private:
    ~Credential();

    void StartAsking();
    static DWORD WINAPI AskThread(void* parameter);
    void Ask();
    void SetStatus(UINT text);
    void ClearPassword();

    LONG _references = 1;
    Provider* _provider;
    ICredentialProviderUser* _user;
    ICredentialProviderCredentialEvents* _events = nullptr;
    std::wstring _sid;
    std::wstring _qualifiedUser;
    std::wstring _status;
    std::wstring _password;
    CRITICAL_SECTION _lock;
    HANDLE _thread = nullptr;
    HANDLE _pipe = nullptr;
    volatile LONG _asking = 0;
    volatile LONG _ready = 0;
};

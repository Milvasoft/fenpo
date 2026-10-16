#pragma once

#include "Common.h"

class Credential;

// Fenpo's credential provider. Only for unlocking a locked session (CPUS_UNLOCK_WORKSTATION), and only when Fenpo in
// that session says it is set up for one of the users on the lock screen; otherwise it shows nothing.
class Provider final : public ICredentialProvider, public ICredentialProviderSetUserArray
{
public:
    Provider();

    // IUnknown
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;

    // ICredentialProvider
    IFACEMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO scenario, DWORD flags) override;
    IFACEMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization) override;
    IFACEMETHODIMP Advise(ICredentialProviderEvents* events, UINT_PTR context) override;
    IFACEMETHODIMP UnAdvise() override;
    IFACEMETHODIMP GetFieldDescriptorCount(DWORD* count) override;
    IFACEMETHODIMP GetFieldDescriptorAt(DWORD index, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** descriptor) override;
    IFACEMETHODIMP GetCredentialCount(DWORD* count, DWORD* defaultIndex, BOOL* autoLogonWithDefault) override;
    IFACEMETHODIMP GetCredentialAt(DWORD index, ICredentialProviderCredential** credential) override;

    // ICredentialProviderSetUserArray
    IFACEMETHODIMP SetUserArray(ICredentialProviderUserArray* users) override;

    // The credential's answer came: LogonUI asks for the credentials again (and logs on when one is ready).
    void CredentialsChanged();

private:
    ~Provider();
    void CreateCredential();
    void ReleaseCredential();

    LONG _references = 1;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _scenario = CPUS_INVALID;
    ICredentialProviderUserArray* _users = nullptr;
    ICredentialProviderEvents* _events = nullptr;
    UINT_PTR _context = 0;
    Credential* _credential = nullptr;
    CRITICAL_SECTION _lock;
};

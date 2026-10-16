// Fenpo credential provider: unlocks the signed-in user's locked session after the phone's Face ID or fingerprint.
// Loaded by LogonUI as SYSTEM. It never stores secrets: Fenpo.exe (in the user's session) decrypts the password with
// the key the phone sends after its biometric check, and hands it over a pipe only LogonUI may open.
#pragma once

#include <windows.h>
#include <credentialprovider.h>
#include <ntsecapi.h>
#include <propkey.h>
#include <shlwapi.h>
#include <strsafe.h>
#include <string>

// {3B6E2B7A-9C41-4D8E-B2F0-6A1C5D7E8F90}
inline constexpr CLSID CLSID_FenpoCredentialProvider = { 0x3b6e2b7a, 0x9c41, 0x4d8e, { 0xb2, 0xf0, 0x6a, 0x1c, 0x5d, 0x7e, 0x8f, 0x90 } };
inline constexpr wchar_t ClassIdText[] = L"{3B6E2B7A-9C41-4D8E-B2F0-6A1C5D7E8F90}";

// The tile's fields.
enum FieldId : DWORD
{
    FieldTileImage = 0,
    FieldTitle = 1,
    FieldStatus = 2,
    FieldRetry = 3,
    FieldCount = 4,
};

void DllAddRef();
void DllRelease();

// Text from the string table in the user's language (Turkish or English).
std::wstring LoadText(UINT id);

// A copy for COM callers, freed with CoTaskMemFree.
HRESULT DuplicateString(const std::wstring& text, PWSTR* result);

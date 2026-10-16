#pragma once

#include "Common.h"

// The pipe Fenpo.exe serves in the user's session: \\.\pipe\fenpo-unlock-<session id>. Fenpo lets only SYSTEM open
// it and answers only LogonUI.exe while the session is locked. One line per request and per answer:
//   STATUS              -> ENROLLED <user SID> | NONE
//   UNLOCK              -> OK <password, UTF-8, base64> | FAIL <declined|notconnected|notpaired|timeout|error>
namespace UnlockPipe
{
    // The SID of the user Fenpo can unlock for, or empty when it cannot (not set up, Fenpo not running). Quick:
    // gives up after half a second.
    std::wstring EnrolledSid();

    // Asks the phone through Fenpo and waits for the answer (up to about a minute). On success the password is in
    // `password` (wiped by the caller); otherwise `reason` says why.
    bool Unlock(std::wstring& password, std::wstring& reason, HANDLE* activePipe);
}

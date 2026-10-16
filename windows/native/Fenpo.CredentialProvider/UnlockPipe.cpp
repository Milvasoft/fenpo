#include "UnlockPipe.h"
#include <wincrypt.h>
#include <vector>

#pragma comment(lib, "crypt32.lib")

namespace
{
    std::wstring PipeName()
    {
        DWORD session = 0;
        ProcessIdToSessionId(GetCurrentProcessId(), &session);
        return L"\\\\.\\pipe\\fenpo-unlock-" + std::to_wstring(session);
    }

    HANDLE Connect(DWORD waitMilliseconds)
    {
        const auto name = PipeName();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            HANDLE pipe = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);

            if (pipe != INVALID_HANDLE_VALUE)
            {
                return pipe;
            }

            if (GetLastError() != ERROR_PIPE_BUSY || !WaitNamedPipeW(name.c_str(), waitMilliseconds))
            {
                return INVALID_HANDLE_VALUE;
            }
        }

        return INVALID_HANDLE_VALUE;
    }

    bool WriteLine(HANDLE pipe, const char* line)
    {
        DWORD written = 0;
        const auto length = static_cast<DWORD>(strlen(line));
        return WriteFile(pipe, line, length, &written, nullptr) && written == length;
    }

    bool ReadLine(HANDLE pipe, std::string& line)
    {
        line.clear();
        char c = 0;
        DWORD read = 0;

        while (line.size() < 8192)
        {
            if (!ReadFile(pipe, &c, 1, &read, nullptr) || read == 0)
            {
                return false;
            }

            if (c == '\n')
            {
                return true;
            }

            if (c != '\r')
            {
                line.push_back(c);
            }
        }

        return false;
    }

    std::wstring Widen(const std::string& text)
    {
        if (text.empty())
        {
            return {};
        }

        const int length = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring result(length, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), length);
        return result;
    }

    bool DecodeBase64(const std::string& text, std::wstring& result)
    {
        DWORD size = 0;

        if (!CryptStringToBinaryA(text.c_str(), static_cast<DWORD>(text.size()), CRYPT_STRING_BASE64, nullptr, &size, nullptr, nullptr))
        {
            return false;
        }

        std::vector<BYTE> bytes(size);

        if (!CryptStringToBinaryA(text.c_str(), static_cast<DWORD>(text.size()), CRYPT_STRING_BASE64, bytes.data(), &size, nullptr, nullptr))
        {
            return false;
        }

        result = Widen(std::string(reinterpret_cast<const char*>(bytes.data()), size));
        SecureZeroMemory(bytes.data(), bytes.size());
        return true;
    }
}

namespace UnlockPipe
{
    std::wstring EnrolledSid()
    {
        HANDLE pipe = Connect(500);

        if (pipe == INVALID_HANDLE_VALUE)
        {
            return {};
        }

        std::string line;
        std::wstring sid;

        if (WriteLine(pipe, "STATUS\n") && ReadLine(pipe, line) && line.rfind("ENROLLED ", 0) == 0)
        {
            sid = Widen(line.substr(9));
        }

        CloseHandle(pipe);
        return sid;
    }

    bool Unlock(std::wstring& password, std::wstring& reason, HANDLE* activePipe)
    {
        HANDLE pipe = Connect(2000);

        if (pipe == INVALID_HANDLE_VALUE)
        {
            reason = L"notconnected";
            return false;
        }

        // Lets SetDeselected cancel the wait by closing the pipe.
        *activePipe = pipe;
        std::string line;
        bool ok = false;

        if (!WriteLine(pipe, "UNLOCK\n") || !ReadLine(pipe, line))
        {
            reason = L"error";
        }
        else if (line.rfind("OK ", 0) == 0)
        {
            ok = DecodeBase64(line.substr(3), password);
            reason = ok ? L"" : L"error";
        }
        else
        {
            reason = line.rfind("FAIL ", 0) == 0 ? Widen(line.substr(5)) : L"error";
        }

        SecureZeroMemory(line.data(), line.size());
        *activePipe = nullptr;
        CloseHandle(pipe);
        return ok;
    }
}

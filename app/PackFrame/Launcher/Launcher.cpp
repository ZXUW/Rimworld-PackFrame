#include <windows.h>
#include <string>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    wchar_t modulePath[32768];
    DWORD length = GetModuleFileNameW(nullptr, modulePath, 32768);
    if (length == 0 || length >= 32768)
    {
        return 1;
    }

    std::wstring root(modulePath, length);
    size_t slash = root.find_last_of(L"\\/");
    if (slash == std::wstring::npos)
    {
        return 1;
    }
    root.erase(slash);

    const std::wstring relative = L"\\app\\PackFrame\\bin\\Release\\net10.0";
    std::wstring target = root + relative + L"\\packframe.exe";
    std::wstring workdir = root + relative;
    if (GetFileAttributesW(target.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        std::wstring message = L"packframe.exe was not found:\n" + target;
        MessageBoxW(nullptr, message.c_str(), L"Rimworld PackFrame", MB_OK | MB_ICONERROR);
        return 1;
    }

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    std::wstring commandLine = L"\"" + target + L"\"";
    if (!CreateProcessW(
            target.c_str(),
            &commandLine[0],
            nullptr,
            nullptr,
            FALSE,
            0,
            nullptr,
            workdir.c_str(),
            &startup,
            &process))
    {
        std::wstring message = L"Could not start packframe.exe:\n" + target;
        MessageBoxW(nullptr, message.c_str(), L"Rimworld PackFrame", MB_OK | MB_ICONERROR);
        return 1;
    }

    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}

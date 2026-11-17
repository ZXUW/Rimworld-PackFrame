#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>
#include <commctrl.h>
#include <shellapi.h>
#include <bcrypt.h>
#include <strsafe.h>
#include <stdlib.h>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "kernel32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "winhttp.lib")
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "bcrypt.lib")

#ifndef PBM_SETPOS
#define PBM_SETPOS (WM_USER + 2)
#define PBM_SETRANGE32 (WM_USER + 6)
#endif

static const wchar_t* kUrls[] = {
    L"https://gitee.com/zxuw/PackFrame-Runtime/releases/download/dotnet-10.0.15/dotnet-runtime-10.0.15-win-x64.exe",
    L"https://github.com/ZXUW/PackFrame-Runtime/releases/download/dotnet-10.0.15/dotnet-runtime-10.0.15-win-x64.exe",
    L"https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.15/dotnet-runtime-10.0.15-win-x64.exe"
};

static const ULONGLONG kExpectedSize = 30661872ULL;
static const BYTE kExpectedSha256[32] = {
    0x59, 0xb0, 0xda, 0x00, 0x3d, 0x18, 0xb7, 0x6e,
    0x8d, 0xcc, 0x0d, 0xc0, 0x5b, 0xf7, 0xe5, 0x4a,
    0x0e, 0x6e, 0x83, 0xe7, 0xc5, 0x69, 0xd5, 0x7e,
    0x1e, 0xe5, 0xc2, 0x70, 0x66, 0x4b, 0xb1, 0x28
};

static int IsChineseLocale(void) {
    LANGID lang = GetUserDefaultUILanguage();
    return PRIMARYLANGID(lang) == LANG_CHINESE;
}

static int HasNet10InFolder(const wchar_t* root) {
    if (!root || !root[0]) return 0;
    wchar_t pattern[MAX_PATH];
    if (FAILED(StringCchPrintfW(pattern, MAX_PATH, L"%s\\shared\\Microsoft.NETCore.App\\10.*", root)))
        return 0;

    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW(pattern, &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    int found = 0;
    do {
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && fd.cFileName[0] != L'.') {
            wchar_t dllPath[MAX_PATH];
            if (SUCCEEDED(StringCchPrintfW(dllPath, MAX_PATH, L"%s\\shared\\Microsoft.NETCore.App\\%s\\hostpolicy.dll", root, fd.cFileName))) {
                if (GetFileAttributesW(dllPath) != INVALID_FILE_ATTRIBUTES) {
                    found = 1;
                    break;
                }
            }
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return found;
}

static int IsNet10Installed(void) {
    wchar_t envRoot[MAX_PATH];
    DWORD len = GetEnvironmentVariableW(L"DOTNET_ROOT", envRoot, MAX_PATH);
    if (len > 0 && len < MAX_PATH && HasNet10InFolder(envRoot)) return 1;
    len = GetEnvironmentVariableW(L"DOTNET_ROOT(x86)", envRoot, MAX_PATH);

    HKEY hKey = NULL;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64", 0, KEY_READ | KEY_WOW64_64KEY, &hKey) == ERROR_SUCCESS) {
        wchar_t installLoc[MAX_PATH] = {0};
        DWORD cb = sizeof(installLoc);
        if (RegQueryValueExW(hKey, L"InstallLocation", NULL, NULL, (LPBYTE)installLoc, &cb) == ERROR_SUCCESS) {
            size_t n = wcslen(installLoc);
            while (n > 0 && (installLoc[n - 1] == L'\\' || installLoc[n - 1] == L'/')) installLoc[--n] = 0;
            if (HasNet10InFolder(installLoc)) {
                RegCloseKey(hKey);
                return 1;
            }
        }
        RegCloseKey(hKey);
    }

    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedhost", 0, KEY_READ | KEY_WOW64_64KEY, &hKey) == ERROR_SUCCESS) {
        wchar_t hostPath[MAX_PATH] = {0};
        DWORD cb = sizeof(hostPath);
        if (RegQueryValueExW(hKey, L"Path", NULL, NULL, (LPBYTE)hostPath, &cb) == ERROR_SUCCESS) {
            size_t n = wcslen(hostPath);
            while (n > 0 && (hostPath[n - 1] == L'\\' || hostPath[n - 1] == L'/')) hostPath[--n] = 0;
            if (HasNet10InFolder(hostPath)) {
                RegCloseKey(hKey);
                return 1;
            }
        }
        RegCloseKey(hKey);
    }

    wchar_t pf[MAX_PATH];
    if (GetEnvironmentVariableW(L"ProgramFiles", pf, MAX_PATH) > 0) {
        wchar_t stdRoot[MAX_PATH];
        if (SUCCEEDED(StringCchPrintfW(stdRoot, MAX_PATH, L"%s\\dotnet", pf)) && HasNet10InFolder(stdRoot))
            return 1;
    }
    if (HasNet10InFolder(L"C:\\Program Files\\dotnet")) return 1;

    wchar_t userProfile[MAX_PATH];
    if (GetEnvironmentVariableW(L"USERPROFILE", userProfile, MAX_PATH) > 0) {
        wchar_t userRoot[MAX_PATH];
        if (SUCCEEDED(StringCchPrintfW(userRoot, MAX_PATH, L"%s\\.dotnet", userProfile)) && HasNet10InFolder(userRoot))
            return 1;
    }
    return 0;
}

static int VerifySha256AndSize(const wchar_t* filePath) {
    HANDLE hFile = CreateFileW(filePath, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) return 0;

    LARGE_INTEGER sz;
    if (!GetFileSizeEx(hFile, &sz) || (ULONGLONG)sz.QuadPart != kExpectedSize) {
        CloseHandle(hFile);
        return 0;
    }

    BCRYPT_ALG_HANDLE hAlg = NULL;
    BCRYPT_HASH_HANDLE hHash = NULL;
    BYTE hash[32];
    int ok = 0;
    BYTE* buf = (BYTE*)HeapAlloc(GetProcessHeap(), 0, 65536);
    if (buf && BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_SHA256_ALGORITHM, NULL, 0))) {
        if (BCRYPT_SUCCESS(BCryptCreateHash(hAlg, &hHash, NULL, 0, NULL, 0, 0))) {
            DWORD readBytes = 0;
            int readOk = 1;
            while (ReadFile(hFile, buf, 65536, &readBytes, NULL) && readBytes > 0) {
                if (!BCRYPT_SUCCESS(BCryptHashData(hHash, buf, readBytes, 0))) {
                    readOk = 0;
                    break;
                }
            }
            if (readOk && BCRYPT_SUCCESS(BCryptFinishHash(hHash, hash, sizeof(hash), 0))) {
                ok = (memcmp(hash, kExpectedSha256, 32) == 0);
            }
            BCryptDestroyHash(hHash);
        }
        BCryptCloseAlgorithmProvider(hAlg, 0);
    }
    if (buf) HeapFree(GetProcessHeap(), 0, buf);
    CloseHandle(hFile);
    return ok;
}

typedef struct {
    HWND hwnd;
    HWND label;
    HWND bar;
    volatile LONG cancel;
    volatile LONG done;
    int success;
    wchar_t installerPath[MAX_PATH];
} DownloadCtx;

#define WM_APP_PROGRESS (WM_APP + 1)
#define WM_APP_DONE     (WM_APP + 2)

static LRESULT CALLBACK ProgressWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    DownloadCtx* ctx = (DownloadCtx*)GetWindowLongPtrW(hwnd, GWLP_USERDATA);
    switch (msg) {
    case WM_CREATE: {
        CREATESTRUCTW* cs = (CREATESTRUCTW*)lp;
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
        return 0;
    }
    case WM_COMMAND:
        if (LOWORD(wp) == IDCANCEL && ctx) {
            InterlockedExchange(&ctx->cancel, 1);
        }
        return 0;
    case WM_CLOSE:
        if (ctx) InterlockedExchange(&ctx->cancel, 1);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

static int DownloadSingleUrl(const wchar_t* startUrl, const wchar_t* outPath, DownloadCtx* ctx, int sourceIdx) {
    HINTERNET hSession = WinHttpOpen(L"Rimworld-PackFrame-Bootstrap/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hSession) return 0;
    WinHttpSetTimeouts(hSession, 6000, 6000, 15000, 30000);
    DWORD redirPolicy = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
    WinHttpSetOption(hSession, WINHTTP_OPTION_REDIRECT_POLICY, &redirPolicy, sizeof(redirPolicy));

    wchar_t currentUrl[2048];
    if (FAILED(StringCchCopyW(currentUrl, 2048, startUrl))) {
        WinHttpCloseHandle(hSession);
        return 0;
    }

    int ok = 0;
    for (int hop = 0; hop < 6 && InterlockedCompareExchange(&ctx->cancel, 0, 0) == 0; hop++) {
        URL_COMPONENTS uc;
        ZeroMemory(&uc, sizeof(uc));
        uc.dwStructSize = sizeof(uc);
        wchar_t host[256] = {0};
        wchar_t path[1792] = {0};
        uc.lpszHostName = host;
        uc.dwHostNameLength = 255;
        uc.lpszUrlPath = path;
        uc.dwUrlPathLength = 1791;
        if (!WinHttpCrackUrl(currentUrl, 0, 0, &uc)) break;

        HINTERNET hConnect = WinHttpConnect(hSession, host, uc.nPort, 0);
        if (!hConnect) break;

        DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET hReq = WinHttpOpenRequest(hConnect, L"GET", path, NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
        if (!hReq) {
            WinHttpCloseHandle(hConnect);
            break;
        }

        int redirected = 0;
        if (WinHttpSendRequest(hReq, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) &&
            WinHttpReceiveResponse(hReq, NULL)) {
            DWORD status = 0, cbStatus = sizeof(status);
            WinHttpQueryHeaders(hReq, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &status, &cbStatus, WINHTTP_NO_HEADER_INDEX);
            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308) {
                wchar_t location[2048] = {0};
                DWORD cbLoc = sizeof(location);
                if (WinHttpQueryHeaders(hReq, WINHTTP_QUERY_LOCATION, WINHTTP_HEADER_NAME_BY_INDEX, location, &cbLoc, WINHTTP_NO_HEADER_INDEX)) {
                    if (wcsncmp(location, L"https://", 8) == 0) {
                        StringCchCopyW(currentUrl, 2048, location);
                        redirected = 1;
                    } else if (location[0] == L'/') {
                        StringCchPrintfW(currentUrl, 2048, L"https://%s%s", host, location);
                        redirected = 1;
                    }
                }
            } else if (status == 200) {
                HANDLE hFile = CreateFileW(outPath, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
                if (hFile != INVALID_HANDLE_VALUE) {
                    BYTE* buf = (BYTE*)HeapAlloc(GetProcessHeap(), 0, 65536);
                    ULONGLONG total = 0;
                    int writeErr = 0;
                    while (buf && InterlockedCompareExchange(&ctx->cancel, 0, 0) == 0) {
                        DWORD avail = 0;
                        if (!WinHttpQueryDataAvailable(hReq, &avail)) { writeErr = 1; break; }
                        if (avail == 0) break;
                        if (avail > 65536) avail = 65536;
                        DWORD read = 0;
                        if (!WinHttpReadData(hReq, buf, avail, &read)) { writeErr = 1; break; }
                        if (read == 0) break;
                        DWORD written = 0;
                        if (!WriteFile(hFile, buf, read, &written, NULL) || written != read) { writeErr = 1; break; }
                        total += written;
                        if (total > kExpectedSize + 1024 * 1024) { writeErr = 1; break; }
                        int pct = (int)((total * 1000ULL) / kExpectedSize);
                        if (pct > 1000) pct = 1000;
                        PostMessageW(ctx->bar, PBM_SETPOS, pct, 0);
                        if (ctx->label) {
                            wchar_t msg[256];
                            if (IsChineseLocale()) {
                                const wchar_t* srcName = sourceIdx == 0 ? L"Gitee 国内镜像" : (sourceIdx == 1 ? L"GitHub 镜像" : L"微软官方源");
                                StringCchPrintfW(msg, 256, L"正在下载 .NET 10 运行时（%s）：%.1f / %.1f MB (%d%%)",
                                    srcName, (double)total / 1048576.0, (double)kExpectedSize / 1048576.0, pct / 10);
                            } else {
                                StringCchPrintfW(msg, 256, L"Downloading .NET 10 Runtime: %.1f / %.1f MB (%d%%)",
                                    (double)total / 1048576.0, (double)kExpectedSize / 1048576.0, pct / 10);
                            }
                            SetWindowTextW(ctx->label, msg);
                        }
                    }
                    if (buf) HeapFree(GetProcessHeap(), 0, buf);
                    CloseHandle(hFile);
                    if (!writeErr && InterlockedCompareExchange(&ctx->cancel, 0, 0) == 0 && VerifySha256AndSize(outPath)) {
                        ok = 1;
                    } else {
                        DeleteFileW(outPath);
                    }
                }
            }
        }

        WinHttpCloseHandle(hReq);
        WinHttpCloseHandle(hConnect);
        if (!redirected) break;
    }

    WinHttpCloseHandle(hSession);
    return ok;
}

static DWORD WINAPI DownloadWorker(LPVOID param) {
    DownloadCtx* ctx = (DownloadCtx*)param;
    if (VerifySha256AndSize(ctx->installerPath)) {
        ctx->success = 1;
        InterlockedExchange(&ctx->done, 1);
        PostMessageW(ctx->hwnd, WM_APP_DONE, 0, 0);
        return 0;
    }

    for (int i = 0; i < 3; i++) {
        if (InterlockedCompareExchange(&ctx->cancel, 0, 0) != 0) break;
        if (DownloadSingleUrl(kUrls[i], ctx->installerPath, ctx, i)) {
            ctx->success = 1;
            break;
        }
    }
    InterlockedExchange(&ctx->done, 1);
    PostMessageW(ctx->hwnd, WM_APP_DONE, 0, 0);
    return 0;
}

static int DownloadAndInstallNet10(HINSTANCE hInst) {
    int zh = IsChineseLocale();
    int ask = MessageBoxW(
        NULL,
        zh ? L"未检测到 .NET 10 运行时（约 29 MB）。\n\n是否立即通过国内镜像自动下载并安装？\n（安装完成后会自动启动 Rimworld PackFrame）"
           : L".NET 10 Runtime (~29 MB) is not installed.\n\nDownload and install it automatically now?",
        L"Rimworld PackFrame",
        MB_YESNO | MB_ICONQUESTION | MB_SETFOREGROUND
    );
    if (ask != IDYES) return 0;

    INITCOMMONCONTROLSEX icc = { sizeof(icc), ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES };
    InitCommonControlsEx(&icc);

    DownloadCtx ctx;
    ZeroMemory(&ctx, sizeof(ctx));
    wchar_t tempDir[MAX_PATH];
    if (GetTempPathW(MAX_PATH, tempDir) == 0) return 0;
    StringCchPrintfW(ctx.installerPath, MAX_PATH, L"%sdotnet-runtime-10.0.15-win-x64.exe", tempDir);

    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = ProgressWndProc;
    wc.hInstance = hInst;
    wc.hCursor = LoadCursorW(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = L"PackFrameBootstrapWnd";
    wc.hIcon = LoadIconW(hInst, MAKEINTRESOURCEW(1));
    RegisterClassExW(&wc);

    int w = 460, h = 155;
    int sx = (GetSystemMetrics(SM_CXSCREEN) - w) / 2;
    int sy = (GetSystemMetrics(SM_CYSCREEN) - h) / 2;
    HWND hwnd = CreateWindowExW(
        WS_EX_DLGMODALFRAME,
        wc.lpszClassName,
        zh ? L"Rimworld PackFrame - 配置运行环境" : L"Rimworld PackFrame - Runtime Setup",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        sx, sy, w, h,
        NULL, NULL, hInst, &ctx
    );
    ctx.hwnd = hwnd;

    HFONT hFont = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");

    ctx.label = CreateWindowExW(0, L"STATIC",
        zh ? L"正在连接国内镜像下载 .NET 10 运行时..." : L"Connecting to download .NET 10 Runtime...",
        WS_CHILD | WS_VISIBLE, 18, 16, 410, 24, hwnd, NULL, hInst, NULL);
    ctx.bar = CreateWindowExW(0, PROGRESS_CLASSW, NULL,
        WS_CHILD | WS_VISIBLE | PBS_SMOOTH, 18, 48, 410, 22, hwnd, NULL, hInst, NULL);
    HWND btnCancel = CreateWindowExW(0, L"BUTTON",
        zh ? L"取消" : L"Cancel",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON, 340, 80, 88, 28, hwnd, (HMENU)IDCANCEL, hInst, NULL);

    SendMessageW(ctx.label, WM_SETFONT, (WPARAM)hFont, TRUE);
    SendMessageW(btnCancel, WM_SETFONT, (WPARAM)hFont, TRUE);
    SendMessageW(ctx.bar, PBM_SETRANGE32, 0, 1000);

    ShowWindow(hwnd, SW_SHOW);
    UpdateWindow(hwnd);

    HANDLE hThread = CreateThread(NULL, 0, DownloadWorker, &ctx, 0, NULL);
    MSG msg;
    while (InterlockedCompareExchange(&ctx.done, 0, 0) == 0 && GetMessageW(&msg, NULL, 0, 0) > 0) {
        if (msg.message == WM_APP_DONE) break;
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    if (hThread) {
        WaitForSingleObject(hThread, INFINITE);
        CloseHandle(hThread);
    }

    DestroyWindow(hwnd);
    if (hFont) DeleteObject(hFont);

    if (InterlockedCompareExchange(&ctx.cancel, 0, 0) != 0) return 0;

    if (!ctx.success) {
        int openWeb = MessageBoxW(
            NULL,
            zh ? L"自动下载 .NET 10 运行时失败。\n\n是否在浏览器中打开国内镜像下载页手动安装？"
               : L"Failed to download .NET 10 Runtime automatically.\n\nOpen the download page in your browser?",
            L"Rimworld PackFrame",
            MB_YESNO | MB_ICONWARNING
        );
        if (openWeb == IDYES) {
            ShellExecuteW(NULL, L"open", L"https://gitee.com/zxuw/PackFrame-Runtime/releases", NULL, NULL, SW_SHOWNORMAL);
        }
        return 0;
    }

    SHELLEXECUTEINFOW sei;
    ZeroMemory(&sei, sizeof(sei));
    sei.cbSize = sizeof(sei);
    sei.fMask = SEE_MASK_NOCLOSEPROCESS;
    sei.lpVerb = L"open";
    sei.lpFile = ctx.installerPath;
    sei.lpParameters = L"/install /passive /norestart";
    sei.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&sei) || !sei.hProcess) {
        MessageBoxW(NULL,
            zh ? L"无法启动 .NET 10 安装程序，请右键管理员运行或手动安装。" : L"Could not launch the .NET 10 installer.",
            L"Rimworld PackFrame", MB_OK | MB_ICONERROR);
        return 0;
    }

    WaitForSingleObject(sei.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(sei.hProcess, &exitCode);
    CloseHandle(sei.hProcess);

    if (exitCode == 0 || exitCode == 3010 || IsNet10Installed()) {
        DeleteFileW(ctx.installerPath);
        return 1;
    }

    MessageBoxW(NULL,
        zh ? L".NET 10 安装未完成，请完成安装后再启动软件。" : L".NET 10 installation did not complete.",
        L"Rimworld PackFrame", MB_OK | MB_ICONWARNING);
    return 0;
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, PWSTR lpCmdLine, int nCmdShow) {
    (void)hPrevInstance;
    (void)nCmdShow;

    SetProcessDPIAware();

    wchar_t selfPath[MAX_PATH];
    DWORD len = GetModuleFileNameW(NULL, selfPath, MAX_PATH);
    if (len == 0 || len >= MAX_PATH) return 1;

    wchar_t* slash = wcsrchr(selfPath, L'\\');
    if (!slash) return 1;
    *slash = 0;

    wchar_t targetExe[MAX_PATH];
    if (FAILED(StringCchPrintfW(targetExe, MAX_PATH, L"%s\\app\\PackFrame\\bin\\Release\\net10.0\\packframe.exe", selfPath)))
        return 1;

    if (GetFileAttributesW(targetExe) == INVALID_FILE_ATTRIBUTES) {
        wchar_t err[MAX_PATH + 128];
        StringCchPrintfW(err, MAX_PATH + 128,
            IsChineseLocale() ? L"未找到主程序文件，请确认压缩包已完整解压：\n%s" : L"packframe.exe was not found:\n%s",
            targetExe);
        MessageBoxW(NULL, err, L"Rimworld PackFrame", MB_OK | MB_ICONERROR);
        return 1;
    }

    wchar_t configJson[MAX_PATH];
    StringCchPrintfW(configJson, MAX_PATH, L"%s\\app\\PackFrame\\bin\\Release\\net10.0\\packframe.json", selfPath);
    if (GetFileAttributesW(configJson) == INVALID_FILE_ATTRIBUTES) {
        if (!IsNet10Installed() && !DownloadAndInstallNet10(hInstance)) {
            return 1;
        }
    }

    wchar_t cmdLine[2048];
    if (lpCmdLine && lpCmdLine[0]) {
        StringCchPrintfW(cmdLine, 2048, L"\"%s\" %s", targetExe, lpCmdLine);
    } else {
        StringCchPrintfW(cmdLine, 2048, L"\"%s\"", targetExe);
    }

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    ZeroMemory(&pi, sizeof(pi));
    si.cb = sizeof(si);

    if (!CreateProcessW(targetExe, cmdLine, NULL, NULL, FALSE, 0, NULL, selfPath, &si, &pi)) {
        wchar_t err[MAX_PATH + 128];
        StringCchPrintfW(err, MAX_PATH + 128,
            IsChineseLocale() ? L"无法启动 packframe.exe（错误码 %lu）：\n%s" : L"Could not start packframe.exe (error %lu):\n%s",
            GetLastError(), targetExe);
        MessageBoxW(NULL, err, L"Rimworld PackFrame", MB_OK | MB_ICONERROR);
        return 1;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}

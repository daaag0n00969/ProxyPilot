#define WIN32_LEAN_AND_MEAN
#include <windows.h>

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR cmd, int show)
{
    WCHAR root[MAX_PATH];
    WCHAR exe[MAX_PATH];
    WCHAR dir[MAX_PATH];
    WCHAR cmdLine[MAX_PATH + 4];
    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    WCHAR *slash;

    (void)inst; (void)prev; (void)cmd; (void)show;

    if (!GetModuleFileNameW(NULL, root, MAX_PATH))
        return 1;
    slash = wcsrchr(root, L'\\');
    if (slash)
        *slash = 0;

    lstrcpynW(dir, root, MAX_PATH);
    lstrcatW(dir, L"\\bin");
    lstrcpynW(exe, dir, MAX_PATH);
    lstrcatW(exe, L"\\ProxyPilot.exe");

    if (GetFileAttributesW(exe) == INVALID_FILE_ATTRIBUTES)
    {
        MessageBoxW(NULL,
            L"Не найден bin\\ProxyPilot.exe.\nПереустановите программу.",
            L"ProxyPilot", MB_ICONERROR);
        return 1;
    }

    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    wsprintfW(cmdLine, L"\"%s\"", exe);
    if (!CreateProcessW(exe, cmdLine, NULL, NULL, FALSE, 0, NULL, dir, &si, &pi))
    {
        MessageBoxW(NULL, L"Не удалось запустить ProxyPilot.", L"ProxyPilot", MB_ICONERROR);
        return 1;
    }
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}

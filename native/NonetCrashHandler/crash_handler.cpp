// SPDX-License-Identifier: GPL-3.0-only
// 精简独立崩溃观察器：阻塞读取专用管道，不轮询、不加载音频、GUI 框架或 .NET。
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#include <wchar.h>
using textchar = wchar_t;
#define TXT(value) L##value
#else
#include <errno.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
using textchar = char;
#define TXT(value) value
#endif

static bool valid_version(const textchar* text) {
    if (!text || !*text) return false;
    for (int i = 0; text[i]; ++i) {
        if (i >= 80 || !((text[i] >= '0' && text[i] <= '9') || (text[i] >= 'a' && text[i] <= 'z') || (text[i] >= 'A' && text[i] <= 'Z') || text[i] == '.' || text[i] == '-' || text[i] == '_')) return false;
    }
    return true;
}

#ifndef _WIN32
// 不调用 shell；错误文本和目录作为 argv 传递，避免路径被解释为命令。
static int run_dialog(const char* executable, char* const arguments[]) {
    const pid_t child = fork();
    if (child == 0) { execvp(executable, arguments); _exit(127); }
    if (child < 0) return 127;
    int status = 0; while (waitpid(child, &status, 0) < 0) { if (errno != EINTR) return 127; }
    return WIFEXITED(status) ? WEXITSTATUS(status) : 127;
}
#endif

static int watch(int argc, textchar** argv) {
    // 参数仅由宿主提供；--quiet 用于自动化故障测试，不展示系统对话框。
    if (argc < 6 || argc > 7 || !valid_version(argv[4])) return 2;
#ifdef _WIN32
    if (wcscmp(argv[1], L"--watch") != 0) return 2;
    wchar_t* end = nullptr; const unsigned long pid = wcstoul(argv[2], &end, 10);
    if (!pid || *end || wcslen(argv[3]) < 3 || wcslen(argv[3]) > 3000 || argv[3][0] == '\\' || argv[3][1] != ':') return 2;
    const bool quiet = argc == 7 && wcscmp(argv[6], L"--quiet") == 0;
    HANDLE process = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    int language = wcsncmp(argv[5], L"en", 2) == 0 ? 1 : wcsncmp(argv[5], L"ja", 2) == 0 ? 2 : 0;
    char marker = 0; DWORD length = 0; BOOL read;
    for (;;) {
        read = ReadFile(GetStdHandle(STD_INPUT_HANDLE), &marker, 1, &length, nullptr);
        if (read && length == 1 && (marker == 'E' || marker == 'J' || marker == 'Z')) { language = marker == 'E' ? 1 : marker == 'J' ? 2 : 0; continue; }
        break;
    }
    if (read && length == 1 && marker == 'C') { if (process) CloseHandle(process); return 0; }
    if (read && length == 1 && marker != 'F') { if (process) CloseHandle(process); return 2; }
    DWORD exit_code = 0xFFFFFFFF;
    if (process) { if (marker != 'F') WaitForSingleObject(process, 5000); GetExitCodeProcess(process, &exit_code); CloseHandle(process); }
    // 正常退出即使来不及发送标志，也不生成错误对话框。
    if (marker != 'F' && exit_code == 0) return 0;
    SYSTEMTIME now; GetLocalTime(&now);
    wchar_t filename[4096];
    if (swprintf(filename, 4096, L"%ls\\%04u-%02u-%02u_%ls.log", argv[3], now.wYear, now.wMonth, now.wDay, argv[4]) < 0) return 3;
    HANDLE file = CreateFileW(filename, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return 3;
    char version[160]; if (!WideCharToMultiByte(CP_UTF8, 0, argv[4], -1, version, 160, nullptr, nullptr)) { CloseHandle(file); return 3; }
    char report[1024]; const int size = snprintf(report, sizeof(report), "%04u-%02u-%02u %02u:%02u:%02u [CRASH] Nonet %s; PID=%lu; reason=%s; exit=0x%08lX\r\nManaged exception details are in the adjacent application log directory.\r\n", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, version, pid, marker == 'F' ? "fatal-reported" : "unexpected-exit", exit_code);
    DWORD written = 0; if (size > 0 && size < static_cast<int>(sizeof(report))) WriteFile(file, report, static_cast<DWORD>(size), &written, nullptr); FlushFileBuffers(file); CloseHandle(file);
    // 仅崩溃时加载系统对话框和文件管理器库，正常待机不承担 GUI DLL 的工作集。
    HMODULE ui = quiet ? nullptr : LoadLibraryExW(L"user32.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    using Metrics = int (WINAPI*)(int); using Dialog = int (WINAPI*)(HWND, LPCWSTR, LPCWSTR, UINT);
    auto metrics = ui ? reinterpret_cast<Metrics>(GetProcAddress(ui, "GetSystemMetrics")) : nullptr;
    auto dialog = ui ? reinterpret_cast<Dialog>(GetProcAddress(ui, "MessageBoxW")) : nullptr;
    if (dialog && metrics && !metrics(SM_SHUTTINGDOWN)) {
        const bool english = language == 1; const bool japanese = language == 2;
        const wchar_t* message = english ? L"Nonet closed unexpectedly. A crash report has been saved.\n\nOpen the report folder?" : japanese ? L"Nonet が予期せず終了しました。クラッシュレポートを保存しました。\n\n保存先を開きますか？" : L"Nonet 意外退出，崩溃记录已保存。\n\n是否打开记录所在的文件夹？";
        if (dialog(nullptr, message, L"Nonet", MB_YESNO | MB_ICONERROR | MB_SETFOREGROUND) == IDYES) {
            HMODULE shell = LoadLibraryExW(L"shell32.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            using Open = HINSTANCE (WINAPI*)(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, int);
            auto open = shell ? reinterpret_cast<Open>(GetProcAddress(shell, "ShellExecuteW")) : nullptr;
            if (open) open(nullptr, L"open", argv[3], nullptr, nullptr, SW_SHOWNORMAL);
            if (shell) FreeLibrary(shell);
        }
    }
    if (ui) FreeLibrary(ui);
#else
    if (strcmp(argv[1], "--watch") != 0 || argv[3][0] != '/' || strlen(argv[3]) > 3000) return 2;
    char* end = nullptr; const unsigned long pid = strtoul(argv[2], &end, 10); if (!pid || *end) return 2;
    const bool quiet = argc == 7 && strcmp(argv[6], "--quiet") == 0;
    int language = strncmp(argv[5], "en", 2) == 0 ? 1 : strncmp(argv[5], "ja", 2) == 0 ? 2 : 0;
    char marker = 0; ssize_t length;
    for (;;) {
        do { length = read(STDIN_FILENO, &marker, 1); } while (length < 0 && errno == EINTR);
        if (length == 1 && (marker == 'E' || marker == 'J' || marker == 'Z')) { language = marker == 'E' ? 1 : marker == 'J' ? 2 : 0; continue; }
        break;
    }
    if (length == 1 && marker == 'C') return 0;
    if (length == 1 && marker != 'F') return 2;
    const time_t clock = time(nullptr); struct tm now; localtime_r(&clock, &now);
    char filename[4096]; if (snprintf(filename, sizeof(filename), "%s/%04d-%02d-%02d_%s.log", argv[3], now.tm_year + 1900, now.tm_mon + 1, now.tm_mday, argv[4]) >= static_cast<int>(sizeof(filename))) return 3;
    FILE* file = fopen(filename, "a"); if (!file) return 3; chmod(filename, S_IRUSR | S_IWUSR);
    fprintf(file, "%04d-%02d-%02d %02d:%02d:%02d [CRASH] Nonet %s; PID=%lu; reason=%s\nManaged exception details are in the adjacent application log directory.\n", now.tm_year + 1900, now.tm_mon + 1, now.tm_mday, now.tm_hour, now.tm_min, now.tm_sec, argv[4], pid, marker == 'F' ? "fatal-reported" : "unexpected-exit"); fflush(file); fsync(fileno(file)); fclose(file);
    if (!quiet) {
        const bool english = language == 1; const bool japanese = language == 2;
        const char* message = english ? "Nonet closed unexpectedly. The crash report was saved to:" : japanese ? "Nonet が予期せず終了しました。クラッシュレポートの保存先：" : "Nonet 意外退出，崩溃记录已保存到：";
        char text[4096]; snprintf(text, sizeof(text), "%s\n%s", message, argv[3]);
#ifdef __APPLE__
        char script[] = "on run argv\ndisplay dialog (item 1 of argv) with title \"Nonet\" buttons {\"OK\"} default button 1 with icon stop\nend run";
        char* args[] = { const_cast<char*>("/usr/bin/osascript"), const_cast<char*>("-e"), script, const_cast<char*>("--"), text, nullptr }; run_dialog(args[0], args);
#else
        char* args[] = { const_cast<char*>("zenity"), const_cast<char*>("--error"), const_cast<char*>("--no-markup"), const_cast<char*>("--title=Nonet"), const_cast<char*>("--text"), text, nullptr };
        if (run_dialog(args[0], args) == 127) { char* fallback[] = { const_cast<char*>("kdialog"), const_cast<char*>("--error"), text, const_cast<char*>("--title"), const_cast<char*>("Nonet"), nullptr }; if (run_dialog(fallback[0], fallback) == 127) fprintf(stderr, "%s\n", text); }
#endif
    }
#endif
    return 1;
}

#ifdef _WIN32
int wmain(int argc, wchar_t** argv) { return watch(argc, argv); }
#else
int main(int argc, char** argv) { return watch(argc, argv); }
#endif

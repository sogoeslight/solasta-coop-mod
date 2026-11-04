/* Downloads logging for the embedded Solasta loader, LGPL-2.1. */
#include <windows.h>
#include <shlobj.h>

extern HANDLE log_handle;
static CRITICAL_SECTION coop_log_sync;
static char coop_early_log[65536];
static DWORD coop_early_bytes, coop_written_bytes;
static BOOL coop_open_attempted;

static void coop_prune_logs(const WCHAR *directory, const WCHAR *current) {
    WCHAR *storage = HeapAlloc(GetProcessHeap(), 0, 3 * 32768 * sizeof(WCHAR));
    if (!storage) return;
    WCHAR *pattern = storage, *oldest = storage + 32768, *candidate = storage + 65536;
    lstrcpyW(pattern, directory);
    lstrcatW(pattern, L"\\doorstop-0.1.22-*.log");
    for (;;) {
        WIN32_FIND_DATAW item;
        HANDLE scan = FindFirstFileW(pattern, &item);
        DWORD count = 0;
        FILETIME earliest = { 0xffffffff, 0xffffffff };
        oldest[0] = 0;
        if (scan == INVALID_HANDLE_VALUE) break;
        do {
            if (!(item.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) {
                count++;
                lstrcpyW(candidate, directory); lstrcatW(candidate, L"\\"); lstrcatW(candidate, item.cFileName);
                if (lstrcmpW(candidate, current) != 0 && CompareFileTime(&item.ftLastWriteTime, &earliest) < 0) {
                    earliest = item.ftLastWriteTime;
                    lstrcpyW(oldest, candidate);
                }
            }
        } while (FindNextFileW(scan, &item));
        FindClose(scan);
        if (count <= 20 || !oldest[0] || !DeleteFileW(oldest)) break;
    }
    HeapFree(GetProcessHeap(), 0, storage);
}

static DWORD WINAPI coop_log_worker(LPVOID ignored) {
    (void)ignored;
    /* This worker runs after DllMain returns. Shell/COM calls never run under the loader lock. */
    HMODULE pinned;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                      (LPCWSTR)&coop_log_worker, &pinned);
    WCHAR *storage = HeapAlloc(GetProcessHeap(), 0, 2 * 32768 * sizeof(WCHAR));
    if (!storage) return 0;
    WCHAR *directory = storage, *file = storage + 32768;
    PWSTR downloads = NULL;
    GUID id = { 0x374de290, 0x123f, 0x4565, { 0x91, 0x64, 0x39, 0xc4, 0x92, 0x5e, 0x46, 0x7b } };
    HRESULT initialized = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    HRESULT found = SHGetKnownFolderPath(&id, 0, NULL, &downloads);
    directory[0] = 0;
    if (SUCCEEDED(found) && downloads && lstrlenW(downloads) < 32000)
        lstrcpyW(directory, downloads);
    else {
        DWORD length = GetEnvironmentVariableW(L"USERPROFILE", directory, 32000);
        if (length && length < 32000) {
            lstrcatW(directory, L"\\Downloads");
            CreateDirectoryW(directory, NULL);
        } else directory[0] = 0;
    }
    if (downloads) CoTaskMemFree(downloads);
    if (SUCCEEDED(initialized)) CoUninitialize();
    HANDLE opened = INVALID_HANDLE_VALUE;
    if (directory[0]) {
        SYSTEMTIME now;
        lstrcatW(directory, L"\\SolastaCoopMod");
        CreateDirectoryW(directory, NULL);
        GetSystemTime(&now);
        WCHAR suffix[128];
        wsprintfW(suffix, L"\\doorstop-0.1.22-%04u%02u%02u-%02u%02u%02u-%lu-%lu.log",
                  now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond,
                  GetCurrentProcessId(), GetTickCount());
        lstrcpyW(file, directory); lstrcatW(file, suffix);
        opened = CreateFileW(file, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    }
    EnterCriticalSection(&coop_log_sync);
    coop_open_attempted = TRUE;
    log_handle = opened;
    if (opened != INVALID_HANDLE_VALUE && coop_early_bytes) {
        DWORD written;
        if (WriteFile(opened, coop_early_log, coop_early_bytes, &written, NULL)) coop_written_bytes = written;
    }
    coop_early_bytes = 0;
    LeaveCriticalSection(&coop_log_sync);
    if (opened != INVALID_HANDLE_VALUE) coop_prune_logs(directory, file);
    HeapFree(GetProcessHeap(), 0, storage);
    return 0;
}

void init_logger(void) {
    InitializeCriticalSection(&coop_log_sync);
    log_handle = INVALID_HANDLE_VALUE;
    HANDLE worker = CreateThread(NULL, 0, coop_log_worker, NULL, 0, NULL);
    if (worker) CloseHandle(worker);
}

void coop_logger_write(const char *data, DWORD bytes) {
    EnterCriticalSection(&coop_log_sync);
    if (log_handle != INVALID_HANDLE_VALUE) {
        if (coop_written_bytes + bytes <= 1048576) {
            DWORD written;
            if (WriteFile(log_handle, data, bytes, &written, NULL)) coop_written_bytes += written;
        }
    } else if (!coop_open_attempted && bytes <= sizeof(coop_early_log) - coop_early_bytes) {
        for (DWORD i = 0; i < bytes; i++) coop_early_log[coop_early_bytes + i] = data[i];
        coop_early_bytes += bytes;
    }
    LeaveCriticalSection(&coop_log_sync);
}

void free_logger(void) {
    EnterCriticalSection(&coop_log_sync);
    if (log_handle != INVALID_HANDLE_VALUE) CloseHandle(log_handle);
    log_handle = INVALID_HANDLE_VALUE;
    LeaveCriticalSection(&coop_log_sync);
}

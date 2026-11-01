#ifndef LOGGER_WIN_H
#define LOGGER_WIN_H
#if VERBOSE

#include "../util/logging.h"
#include "../util/util.h"
#include <windows.h>

extern HANDLE log_handle;
extern char_t buffer[4096];

#ifdef UNICODE
#define printf wsprintfW
#else
#define printf wsprintfA
#endif

void init_logger(void);
void free_logger(void);
void coop_logger_write(const char *data, DWORD bytes);

#if !defined(_MSVC_TRADITIONAL) || _MSVC_TRADITIONAL
#define LOG(message, ...)                                                      \
    {                                                                          \
        char_t coop_line[4096];                                                \
        printf(coop_line, TEXT(message) TEXT("\n"), ##__VA_ARGS__);             \
        char *log_data = narrow(coop_line);                                    \
        DWORD coop_bytes = 0;                                                 \
        while (log_data[coop_bytes]) coop_bytes++;                             \
        coop_logger_write(log_data, coop_bytes);                               \
        free(log_data);                                                        \
    }
#else
#define LOG(message, ...)                                                      \
    {                                                                          \
        char_t coop_line[4096];                                                \
        printf(coop_line, TEXT(message) TEXT("\n") __VA_OPT__(, ) __VA_ARGS__); \
        char *log_data = narrow(coop_line);                                    \
        DWORD coop_bytes = 0;                                                 \
        while (log_data[coop_bytes]) coop_bytes++;                             \
        coop_logger_write(log_data, coop_bytes);                               \
        free(log_data);                                                        \
    }
#endif

#define ASSERT_F(test, message, ...)                                           \
    if (!(test)) {                                                             \
        char_t *buff = (char_t *)malloc(sizeof(char_t) * 1024);                \
        printf(buff, TEXT(message) TEXT("\n"), __VA_ARGS__);                              \
        MessageBox(NULL, buff, TEXT("Doorstop: Fatal"), MB_OK | MB_ICONERROR); \
        free(buff);                                                            \
        ExitProcess(EXIT_FAILURE);                                             \
    }

#define ASSERT(test, message)                                                  \
    if (!(test)) {                                                             \
        MessageBox(NULL, TEXT(message) TEXT("\n"), TEXT("Doorstop: Fatal"),               \
                   MB_OK | MB_ICONERROR);                                      \
        ExitProcess(EXIT_FAILURE);                                             \
    }

#define ASSERT_SOFT(test, ...)                                                 \
    if (!(test)) {                                                             \
        return __VA_ARGS__;                                                    \
    }

#endif
#endif

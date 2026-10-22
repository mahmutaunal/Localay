#pragma once

#ifdef _WIN32
#define LOCALAY_API __declspec(dllexport)
#else
#define LOCALAY_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#define LOCALAY_NOEXCEPT noexcept
#else
#define LOCALAY_NOEXCEPT
#endif

// UTF-8 NUL-terminated strings; count is the exact input/output array size.
// Caller serializes access to context. release belongs to the allocating module.
LOCALAY_API int localay_marian_open_v1(const char*, const char*, const char*, int,
    void**, char*, int) LOCALAY_NOEXCEPT;
LOCALAY_API int localay_marian_translate_v1(void*, const char* const*, int,
    char***, char*, int) LOCALAY_NOEXCEPT;
LOCALAY_API void localay_marian_release_v1(char**, int) LOCALAY_NOEXCEPT;
LOCALAY_API void localay_marian_close_v1(void*) LOCALAY_NOEXCEPT;

#ifdef __cplusplus
}
#endif

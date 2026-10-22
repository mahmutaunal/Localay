#include "../../../native/Marian/localay_marian.h"
#include <cstdlib>
#include <cstring>
#include <string>
#include <mutex>
#include <condition_variable>

// Test double for the ABI only: no inference engine or model dependency.
static int live = 0, buffers = 0, calls = 0, mode = 0;
static std::mutex gate;
static std::condition_variable wake;
static bool blocked = false;
static void (*entered)() = nullptr;
extern "C" LOCALAY_API int fixture_stat(int which) { return which == 0 ? live : which == 1 ? buffers : calls; }
extern "C" LOCALAY_API void fixture_mode(int value) { mode = value; }
extern "C" LOCALAY_API void fixture_block(int value, void (*callback)()) {
    std::lock_guard<std::mutex> lock(gate);
    blocked = value != 0; entered = callback; wake.notify_all();
}
static int fail(char* error, int capacity, const char* message) {
    if (capacity > 0) { std::strncpy(error, message, capacity - 1); error[capacity - 1] = 0; }
    return 1;
}
LOCALAY_API int localay_marian_open_v1(const char*, const char*, const char*, int,
    void** context, char* error, int capacity) noexcept {
    *context = new int(1); ++live;
    return mode == 4 ? fail(error, capacity, "open failed") : 0;
}
LOCALAY_API void localay_marian_close_v1(void* context) noexcept { delete static_cast<int*>(context); --live; }
LOCALAY_API void localay_marian_release_v1(char** output, int count) noexcept {
    for (int i = 0; i < count; ++i) std::free(output[i]);
    std::free(output); --buffers;
}
LOCALAY_API int localay_marian_translate_v1(void*, const char* const* input, int count,
    char*** output, char* error, int capacity) noexcept {
    ++calls;
    {
        std::unique_lock<std::mutex> lock(gate);
        if (entered) entered();
        wake.wait(lock, [] { return !blocked; });
    }
    *output = static_cast<char**>(std::calloc(count, sizeof(char*))); ++buffers;
    for (int i = 0; i < count; ++i) {
        if (mode == 1) continue;
        auto value = std::string("çeviri: ") + input[i];
        (*output)[i] = static_cast<char*>(std::malloc(value.size() + 1));
        std::memcpy((*output)[i], value.c_str(), value.size() + 1);
    }
    if (mode == 2) return fail(error, capacity, "long native failure message");
    if (mode == 3) return fail(error, capacity, "short");
    return 0;
}

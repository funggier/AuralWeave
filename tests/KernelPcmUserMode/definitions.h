#pragma once
// User-mode replacements for the WDK primitives used by aw_kernel_pcm_queue.cpp.
// This header is only on the standalone test compiler's include path.
#include <cstddef>
#include <cstring>
#include <mutex>

using KSPIN_LOCK = std::mutex;
using KIRQL = unsigned char;
using SIZE_T = std::size_t;

inline void KeInitializeSpinLock(KSPIN_LOCK*) {}
inline void KeAcquireSpinLock(KSPIN_LOCK* lock, KIRQL* previous) {
    lock->lock();
    *previous = 0;
}
inline void KeReleaseSpinLock(KSPIN_LOCK* lock, KIRQL) {
    lock->unlock();
}
inline void RtlZeroMemory(void* destination, SIZE_T bytes) {
    std::memset(destination, 0, bytes);
}

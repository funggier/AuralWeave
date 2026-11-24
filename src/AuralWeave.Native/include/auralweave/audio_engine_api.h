#pragma once
#include <cstdint>
#include <cstddef>

#if defined(_WIN32)
#define AW_API extern "C" __declspec(dllexport)
#else
#define AW_API extern "C"
#endif

// Stable C ABI for an isolated native WASAPI streaming prototype.
// Result 0 = failure, 1 = success. Caller owns aw_create/aw_destroy lifetime.
// Source: "capture:<MMDevice ID>" or "loopback:<MMDevice ID>".
// Render devices: raw Windows IMMDevice endpoint IDs.
AW_API void* aw_create() noexcept;
AW_API void aw_destroy(void* handle) noexcept;
AW_API int aw_start(void* handle, const wchar_t* source_id,
                    const wchar_t* const* endpoint_ids,
                    const float* gains, const int* muted, int output_count) noexcept;
AW_API void aw_stop(void* handle) noexcept;
AW_API int aw_state(void* handle) noexcept; // 0 stopped,1 starting,2 running,3 error
AW_API std::uint64_t aw_captured_frames(void* handle) noexcept;
AW_API std::uint64_t aw_dropped_render_frames(void* handle) noexcept;
AW_API float aw_input_peak(void* handle) noexcept;
AW_API int aw_set_effects(void* handle, const float* gains, const int* enabled, int count) noexcept;
AW_API int aw_set_output(void* handle, int index, float gain, int muted) noexcept;
AW_API void aw_last_error(void* handle, wchar_t* buffer, std::size_t capacity) noexcept;

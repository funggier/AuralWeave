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
AW_API std::uint64_t aw_underrun_render_frames(void* handle) noexcept;
AW_API float aw_input_peak(void* handle) noexcept;
// Ordered native filter chain. Empty chain is a DSP bypass.
// Original layout: Gain=1, RNNoise=2, Gate=3, Limiter=4, one float per stage.
AW_API int aw_set_filter_chain(void* handle, const int* kinds, const float* parameters,
                               const int* enabled, int count) noexcept;
// v2 layout: five consecutive floats per stage, unused fields zero.
// Equalizer=5: low/mid/high gain (dB). Compressor=6: threshold (dBFS),
// ratio, attack (ms), release (ms), makeup gain (dB).
// Pitch Shift=7: semitones. All scalar effects use the first float.
AW_API int aw_set_filter_chain_v2(void* handle, const int* kinds, const float* parameters,
                                  const int* enabled, int count) noexcept;
// 0 = DSP bypass; 1 = built-in RNNoise v0.1.1 standard model.
AW_API int aw_set_noise_mode(void* handle, int mode) noexcept;
AW_API int aw_set_effects(void* handle, const float* gains, const int* enabled, int count) noexcept;
AW_API int aw_set_output(void* handle, int index, float gain, int muted) noexcept;
AW_API void aw_last_error(void* handle, wchar_t* buffer, std::size_t capacity) noexcept;

// Native Windows Core Audio endpoints (kind 0=capture, 1=render).
// Query names/IDs, default endpoint and supported shared mix rate/channels.
AW_API int aw_endpoint_count(int kind) noexcept;
AW_API int aw_endpoint_get(int kind, int index,
                           wchar_t* id, std::size_t id_capacity,
                           wchar_t* name, std::size_t name_capacity,
                           int* is_default, int* sample_rate,
                           int* channels) noexcept;

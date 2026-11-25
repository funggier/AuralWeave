#pragma once
// Stable native C ABI for Windows user-mode PCM bridge, mapping protocol v2.
// AuralWeave original source (MIT). No signed kernel driver is required.
// This API does NOT create a Windows playback/recording device.
#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define AW_BRIDGE_API extern "C" __declspec(dllexport)
#else
#define AW_BRIDGE_API extern "C"
#endif

// role: 1 = Windows virtual microphone input, 2 = virtual speaker output.
// slot: 1 only for each direction (single endpoint per role). PCM is interleaved Float32.
// mapping_name: WCHAR buffer >= 160 is recommended; same-session random name.
// Caller must close the opaque handle. At most one writer and one reader
// are permitted per mapping (no concurrent writers).
AW_BRIDGE_API void* aw_bridge_create(
    uint32_t role, uint32_t slot, uint32_t sample_rate, uint32_t channels,
    uint32_t capacity_frames, wchar_t* mapping_name, uint32_t name_chars) noexcept;

AW_BRIDGE_API void* aw_bridge_attach(
    const wchar_t* exact_name, uint32_t expected_role, uint32_t expected_slot) noexcept;

AW_BRIDGE_API void aw_bridge_close(void* handle) noexcept;
AW_BRIDGE_API uint32_t aw_bridge_write(
    void* handle, const float* samples, uint32_t frames) noexcept;
AW_BRIDGE_API uint32_t aw_bridge_read(
    void* handle, float* samples, uint32_t frames, int silence_on_underrun) noexcept;

struct AwVirtualBridgeStats {
    uint64_t frames_written;
    uint64_t frames_read;
    uint64_t overflow_frames;
    uint64_t underflow_frames;
    uint32_t queued_frames;
    uint32_t closed_by_owner;
    uint32_t sample_rate;
    uint32_t channels;
    uint32_t capacity_frames;
};
AW_BRIDGE_API int aw_bridge_get_stats(
    void* handle, AwVirtualBridgeStats* out_stats) noexcept;

// Advisory owner PID for non-elevated same-user mock admission only.
// Not a security token or trusted authorization evidence.
AW_BRIDGE_API uint32_t aw_bridge_owner_process_id(void* handle) noexcept;

// Error string for failed create/attach on this calling thread.
// Length includes terminating NUL; failure returns 0.
AW_BRIDGE_API uint32_t aw_bridge_last_error(
    wchar_t* buffer, uint32_t buffer_chars) noexcept;

// Optional Native Studio PCM tap. Create the virtual INPUT mapping first,
// start a physical Studio route, then bind it. Studio copies the post-filter
// stream to this shared ring as an additional output without changing the
// selected physical output devices. No kernel driver is attached yet.
AW_BRIDGE_API int aw_route_bind_virtual_input(
    void* native_route, const wchar_t* exact_name, uint32_t input_slot) noexcept;
AW_BRIDGE_API void aw_route_unbind_virtual_input(void* native_route) noexcept;

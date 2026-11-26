#pragma once

#include <cstddef>
#include <cstdint>

// VST 2.4 interoperability layout and opcode values referenced from the
// MIT-licensed RustAudio/vst-rs project. See third_party/vst-rs/LICENSE.
namespace auralweave::vst2 {

struct Effect;
using Dispatch = std::intptr_t (__cdecl*)(Effect*, std::int32_t, std::int32_t,
                                         std::intptr_t, void*, float);
using Process = void (__cdecl*)(Effect*, float**, float**, std::int32_t);
using Entry = Effect* (__cdecl*)(Dispatch);

struct Effect {
    std::int32_t magic;
    Dispatch dispatcher;
    Process process_accumulating;
    void (__cdecl* set_parameter)(Effect*, std::int32_t, float);
    float (__cdecl* get_parameter)(Effect*, std::int32_t);
    std::int32_t programs, parameters, inputs, outputs, flags;
    std::intptr_t reserved1, reserved2;
    std::int32_t latency, real_qualities, off_qualities;
    float io_ratio;
    void* object;
    void* user;
    std::int32_t unique_id, version;
    Process process_replacing;
    void (__cdecl* process_double)(Effect*, double**, double**, std::int32_t);
    char reserved[56];
};
static_assert(sizeof(void*) == 8 && sizeof(Effect) == 192 &&
              offsetof(Effect, process_replacing) == 120);

struct EditorRect {
    std::int16_t top, left, bottom, right;
};

struct TimeInfo {
    double sample_position{}, sample_rate{}, nanoseconds{}, quarter_notes{}, tempo{};
    double bar_start{}, cycle_start{}, cycle_end{};
    std::int32_t time_signature_top{}, time_signature_bottom{}, smpte_offset{};
    std::int32_t smpte_rate{}, samples_to_clock{}, flags{};
};

enum EffectOp : std::int32_t {
    open = 0, close = 1, set_sample_rate = 10, set_block_size = 11, set_active = 12,
    edit_rectangle = 13, edit_open = 14, edit_close = 15, edit_idle = 19,
    start_process = 71, stop_process = 72
};

enum HostOp : std::int32_t {
    host_version = 1, host_idle = 3, get_time = 7, size_window = 15, get_sample_rate = 16,
    get_block_size = 17, get_process_level = 23, get_automation_state = 24,
    get_vendor_name = 32, get_product_name = 33, get_vendor_version = 34,
    can_do = 37, get_language = 38
};

constexpr std::int32_t effect_magic = 0x56737450;
constexpr std::int32_t has_editor = 1;
constexpr std::int32_t can_replace = 1 << 4;
constexpr std::int32_t is_synth = 1 << 8;
constexpr std::int32_t transport_playing = 1 << 1;
constexpr std::int32_t quarter_notes_valid = 1 << 9;
constexpr std::int32_t tempo_valid = 1 << 10;
constexpr std::int32_t bars_valid = 1 << 11;
constexpr std::int32_t time_signature_valid = 1 << 13;

} // namespace auralweave::vst2

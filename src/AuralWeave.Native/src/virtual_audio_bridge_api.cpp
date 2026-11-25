#include "auralweave/virtual_audio_bridge_api.h"
#include "auralweave/virtual_audio_bridge.hpp"

#include <algorithm>
#include <cwchar>
#include <exception>
#include <memory>
#include <new>
#include <stdexcept>
#include <string>
#include <utility>

namespace {

struct BridgeHandle {
    explicit BridgeHandle(auralweave::VirtualAudioBridge&& in) :
        bridge(std::move(in)) {}
    auralweave::VirtualAudioBridge bridge;
};

thread_local std::wstring bridge_error;

void report_error(const std::exception& ex) {
    bridge_error.clear();
    for (unsigned char ch : std::string(ex.what()))
        bridge_error.push_back(static_cast<wchar_t>(ch));
}

void report_unknown() {
    bridge_error = L"An unexpected native PCM bridge error occurred";
}

} // namespace

AW_BRIDGE_API void* aw_bridge_create(
    uint32_t role, uint32_t slot, uint32_t sample_rate, uint32_t channels,
    uint32_t capacity_frames, wchar_t* mapping_name,
    uint32_t name_chars) noexcept {
    if (!mapping_name || name_chars == 0) {
        bridge_error = L"A writable mapping name output buffer is required";
        return nullptr;
    }
    mapping_name[0] = L'\0';
    try {
        if (channels > 2)
            throw std::invalid_argument("Only one/two channels supported");
        const auralweave::VirtualBridgeFormat format{
            sample_rate, static_cast<std::uint16_t>(channels), capacity_frames};
        auto bridge = auralweave::VirtualAudioBridge::create(
            static_cast<auralweave::VirtualEndpointRole>(role), slot, format);
        const auto& name = bridge.name();
        if (name.size() + 1 > name_chars)
            throw std::invalid_argument("Mapping name output buffer is too small");
        wmemcpy(mapping_name, name.c_str(), name.size() + 1);
        bridge_error.clear();
        return new BridgeHandle(std::move(bridge));
    } catch (const std::exception& ex) {
        report_error(ex);
    } catch (...) {
        report_unknown();
    }
    mapping_name[0] = L'\0';
    return nullptr;
}

AW_BRIDGE_API void* aw_bridge_attach(
    const wchar_t* exact_name, uint32_t expected_role,
    uint32_t expected_slot) noexcept {
    if (!exact_name) {
        bridge_error = L"PCM mapping name is required";
        return nullptr;
    }
    try {
        auto bridge = auralweave::VirtualAudioBridge::attach(
            exact_name, static_cast<auralweave::VirtualEndpointRole>(
                expected_role), expected_slot);
        bridge_error.clear();
        return new BridgeHandle(std::move(bridge));
    } catch (const std::exception& ex) {
        report_error(ex);
    } catch (...) {
        report_unknown();
    }
    return nullptr;
}

AW_BRIDGE_API void aw_bridge_close(void* handle) noexcept {
    delete static_cast<BridgeHandle*>(handle);
}

AW_BRIDGE_API uint32_t aw_bridge_write(
    void* handle, const float* samples, uint32_t frames) noexcept {
    return handle ? static_cast<BridgeHandle*>(handle)->bridge.write(samples, frames) : 0;
}

AW_BRIDGE_API uint32_t aw_bridge_read(
    void* handle, float* samples, uint32_t frames,
    int silence_on_underrun) noexcept {
    return handle ? static_cast<BridgeHandle*>(handle)->bridge.read(
        samples, frames, silence_on_underrun != 0) : 0;
}

AW_BRIDGE_API int aw_bridge_get_stats(
    void* handle, AwVirtualBridgeStats* out_stats) noexcept {
    if (!handle || !out_stats) return 0;
    const auto& bridge = static_cast<BridgeHandle*>(handle)->bridge;
    const auto stats = bridge.counters();
    const auto format = bridge.format();
    *out_stats = {stats.written_frames, stats.read_frames,
                  stats.overflow_frames, stats.underflow_frames,
                  stats.queued_frames, stats.producer_closed ? 1u : 0u,
                  format.sample_rate, format.channels, format.capacity_frames};
    return 1;
}

AW_BRIDGE_API uint32_t aw_bridge_owner_process_id(void* handle) noexcept {
    return handle ? static_cast<BridgeHandle*>(handle)->bridge.owner_process_id() : 0;
}

AW_BRIDGE_API uint32_t aw_bridge_last_error(
    wchar_t* buffer, uint32_t buffer_chars) noexcept {
    if (!buffer || !buffer_chars) return 0;
    buffer[0] = L'\0';
    if (bridge_error.empty()) return 1;
    if (bridge_error.size() >= buffer_chars) return 0;
    wmemcpy(buffer, bridge_error.c_str(), bridge_error.size() + 1);
    return static_cast<uint32_t>(bridge_error.size() + 1);
}

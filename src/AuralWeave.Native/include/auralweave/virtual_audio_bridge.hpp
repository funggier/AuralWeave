#pragma once
// AuralWeave native user-mode PCM transport scaffold.
// Not yet a Windows kernel-driver transport. Safe to test independently.
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <string_view>

namespace auralweave {

enum class VirtualEndpointRole : std::uint32_t {
    input = 1,   // Audio written by Studio for an eventual virtual microphone.
    output = 2   // Audio rendered by another app to an eventual virtual speaker.
};

struct VirtualBridgeFormat {
    std::uint32_t sample_rate{48000};
    std::uint16_t channels{2};
    std::uint32_t capacity_frames{4096};
};

struct VirtualBridgeCounters {
    std::uint64_t written_frames{};
    std::uint64_t read_frames{};
    std::uint64_t overflow_frames{};
    std::uint64_t underflow_frames{};
    std::uint32_t queued_frames{};
    bool producer_closed{};
};

// A versioned, SPSC (single-producer / single-consumer) mapping.
//
// - Entire PCM frame is the atomic unit; sample format is interleaved Float32.
// - One owner creates a random, Windows-session-local mapping; a peer opens it
//   using its exact name. Do NOT invent a fixed predictable mapping identifier.
// - Endpoint slot is fixed to 1 for each direction.
// - One writer and one reader PER mapping. Duplicating writers/readers is not
//   supported and must be forbidden by the future routing control plane.
// - No locks, allocation, waiting or syscalls on write/read hot paths.
// - PnP and driver-side lifecycle/authorization are not implemented by this class.
class VirtualAudioBridge final {
public:
    static VirtualAudioBridge create(VirtualEndpointRole role, std::uint32_t slot,
                                     VirtualBridgeFormat format = {});
    static VirtualAudioBridge attach(std::wstring_view exact_name,
                                     VirtualEndpointRole expected_role,
                                     std::uint32_t expected_slot);

    VirtualAudioBridge(VirtualAudioBridge&&) noexcept;
    VirtualAudioBridge& operator=(VirtualAudioBridge&&) noexcept;
    VirtualAudioBridge(const VirtualAudioBridge&) = delete;
    VirtualAudioBridge& operator=(const VirtualAudioBridge&) = delete;
    ~VirtualAudioBridge();

    [[nodiscard]] const std::wstring& name() const noexcept { return name_; }
    [[nodiscard]] VirtualEndpointRole role() const noexcept { return role_; }
    [[nodiscard]] std::uint32_t slot() const noexcept { return slot_; }
    [[nodiscard]] VirtualBridgeFormat format() const noexcept { return format_; }
    // Advisory origin PID carried inside user-writable shared memory.
    // Use only for same-user mock admission; NEVER as privileged authorization.
    [[nodiscard]] std::uint32_t owner_process_id() const noexcept;

    // Returns frames transferred; may be partial if buffer is full/empty.
    std::uint32_t write(const float* interleaved, std::uint32_t frames) noexcept;
    std::uint32_t read(float* interleaved, std::uint32_t frames,
                       bool pad_with_silence = false) noexcept;
    [[nodiscard]] VirtualBridgeCounters counters() const noexcept;

    // A clean owner shutdown signals peers. Process crashes are handled by
    // Windows mapping lifetime (no named-object file persists on disk).
    void close() noexcept;
private:
    struct State;
    explicit VirtualAudioBridge(std::unique_ptr<State> state, std::wstring name,
                                VirtualEndpointRole role, std::uint32_t slot,
                                VirtualBridgeFormat format);
    std::unique_ptr<State> state_;
    std::wstring name_;
    VirtualEndpointRole role_{};
    std::uint32_t slot_{};
    VirtualBridgeFormat format_{};
};

} // namespace auralweave

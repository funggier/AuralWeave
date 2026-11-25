#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>

#include "auralweave/virtual_audio_bridge.hpp"

#include <algorithm>
#include <cstddef>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace auralweave {
namespace {
constexpr std::uint32_t bridge_magic = 0x57415241u; // ARAW, not a disk format
constexpr std::uint32_t bridge_version = 1;
constexpr wchar_t bridge_prefix[] = L"Local\\AuralWeave.PCM.v1.";
// An owner-only kernel HANDLE disappears on process termination even if
// attached readers keep the shared PCM mapping alive.
constexpr wchar_t lifetime_suffix[] = L".OwnerAlive";

[[nodiscard]] bool owner_alive(std::wstring_view mapping_name) noexcept {
    const std::wstring alive_name = std::wstring(mapping_name) + lifetime_suffix;
    const HANDLE event = OpenEventW(SYNCHRONIZE, FALSE, alive_name.c_str());
    if (!event) return false; // fail closed for missing names or denied access
    CloseHandle(event);
    return true;
}

struct alignas(64) SharedHeader {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t role;
    std::uint32_t slot;
    std::uint32_t sample_rate;
    std::uint32_t channels;
    std::uint32_t capacity_frames;
    std::uint32_t reserved;
    // Interlocked* is used rather than std::atomic for interprocess mapping.
    alignas(8) volatile LONG64 written;
    alignas(8) volatile LONG64 read;
    alignas(8) volatile LONG64 overflow;
    alignas(8) volatile LONG64 underflow;
    alignas(4) volatile LONG closed;
    // Explicitly fill the second cache line; avoid hidden compiler padding.
    std::byte reserved_padding[60]{};
};
static_assert(sizeof(SharedHeader) % 64 == 0);
static_assert(alignof(SharedHeader) == 64);

[[nodiscard]] LONG64 load64(volatile LONG64* value) noexcept {
    return InterlockedCompareExchange64(value, 0, 0);
}
[[nodiscard]] LONG load32(volatile LONG* value) noexcept {
    return InterlockedCompareExchange(value, 0, 0);
}
[[nodiscard]] bool valid_role(VirtualEndpointRole role) noexcept {
    return role == VirtualEndpointRole::input ||
           role == VirtualEndpointRole::output;
}
[[nodiscard]] bool valid_format(VirtualBridgeFormat f) noexcept {
    return f.sample_rate >= 8000 && f.sample_rate <= 384000 &&
           (f.channels == 1 || f.channels == 2) &&
           f.capacity_frames >= 64 && f.capacity_frames <= 32768;
}
void check_identity(VirtualEndpointRole role, std::uint32_t slot) {
    if (!valid_role(role) || slot != 1)
        throw std::invalid_argument("Virtual endpoint requires input/output role and singleton slot 1");
}
[[nodiscard]] std::wstring new_name(VirtualEndpointRole role,
                                    std::uint32_t slot) {
    GUID guid{};
    if (FAILED(CoCreateGuid(&guid)))
        throw std::runtime_error("CoCreateGuid failed for PCM session name");
    wchar_t text[40]{};
    if (!StringFromGUID2(guid, text, static_cast<int>(std::size(text))))
        throw std::runtime_error("StringFromGUID2 failed");
    return std::wstring(bridge_prefix) +
        (role == VirtualEndpointRole::input ? L"IN." : L"OUT.") +
        std::to_wstring(slot) + L"." + text;
}
[[nodiscard]] std::uint64_t size_bytes(VirtualBridgeFormat format) noexcept {
    return sizeof(SharedHeader) +
           static_cast<std::uint64_t>(format.capacity_frames) *
           format.channels * sizeof(float);
}
[[nodiscard]] bool valid_name(std::wstring_view name) noexcept {
    const std::wstring_view prefix(bridge_prefix);
    if (!name.starts_with(prefix) || name.size() <= prefix.size() + 10 ||
        name.size() > 160)
        return false;
    // Names must be in this session's Local\ namespace, never Global\.
    for (std::size_t i = prefix.size(); i < name.size(); ++i)
        if (name[i] == L'\\' || name[i] == L'/')
            return false;
    return true;
}
} // namespace

struct VirtualAudioBridge::State {
    HANDLE file_mapping{};
    HANDLE owner_lifetime{}; // never duplicated into a peer
    SharedHeader* header{};
    float* pcm{};
    bool owner{};

    ~State() {
        if (header && owner) {
            InterlockedExchange(&header->closed, 1);
        }
        if (owner_lifetime) CloseHandle(owner_lifetime);
        if (header) UnmapViewOfFile(header);
        if (file_mapping) CloseHandle(file_mapping);
    }
};

VirtualAudioBridge::VirtualAudioBridge(std::unique_ptr<State> state,
                                       std::wstring name, VirtualEndpointRole role,
                                       std::uint32_t slot, VirtualBridgeFormat format) :
    state_(std::move(state)), name_(std::move(name)), role_(role),
    slot_(slot), format_(format) {}

VirtualAudioBridge::~VirtualAudioBridge() = default;
VirtualAudioBridge::VirtualAudioBridge(VirtualAudioBridge&&) noexcept = default;
VirtualAudioBridge& VirtualAudioBridge::operator=(VirtualAudioBridge&&) noexcept = default;

VirtualAudioBridge VirtualAudioBridge::create(VirtualEndpointRole role,
                                               std::uint32_t slot,
                                               VirtualBridgeFormat format) {
    check_identity(role, slot);
    if (!valid_format(format))
        throw std::invalid_argument("Invalid shared PCM format/capacity");
    auto state = std::make_unique<State>();
    const auto name = new_name(role, slot);
    const auto bytes = size_bytes(format);
    state->file_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr,
        PAGE_READWRITE, static_cast<DWORD>(bytes >> 32),
        static_cast<DWORD>(bytes & 0xffffffffu), name.c_str());
    if (!state->file_mapping)
        throw std::runtime_error("CreateFileMappingW failed for PCM bridge");
    if (GetLastError() == ERROR_ALREADY_EXISTS)
        throw std::runtime_error("Refusing to reuse existing PCM bridge mapping");

    const auto alive_name = name + lifetime_suffix;
    state->owner_lifetime = CreateEventW(nullptr, TRUE, FALSE, alive_name.c_str());
    if (!state->owner_lifetime || GetLastError() == ERROR_ALREADY_EXISTS)
        throw std::runtime_error("Could not create exclusive PCM owner lifetime event");

    state->header = static_cast<SharedHeader*>(MapViewOfFile(
        state->file_mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0));
    if (!state->header) throw std::runtime_error("MapViewOfFile failed");
    state->owner = true;
    state->pcm = reinterpret_cast<float*>(
        reinterpret_cast<std::byte*>(state->header) + sizeof(SharedHeader));
    // Header and storage begin zeroed for a newly allocated pagefile mapping.
    state->header->version = bridge_version;
    state->header->role = static_cast<std::uint32_t>(role);
    state->header->slot = slot;
    state->header->sample_rate = format.sample_rate;
    state->header->channels = format.channels;
    state->header->capacity_frames = format.capacity_frames;
    InterlockedExchange(reinterpret_cast<volatile LONG*>(&state->header->magic),
                        static_cast<LONG>(bridge_magic)); // publish after initialization
    return VirtualAudioBridge(std::move(state), name, role, slot, format);
}

VirtualAudioBridge VirtualAudioBridge::attach(std::wstring_view exact_name,
                                               VirtualEndpointRole expected_role,
                                               std::uint32_t expected_slot) {
    check_identity(expected_role, expected_slot);
    if (!valid_name(exact_name))
        throw std::invalid_argument("Invalid or unscoped PCM mapping name");
    auto state = std::make_unique<State>();
    const std::wstring name(exact_name);
    state->file_mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE,
                                          FALSE, name.c_str());
    if (!state->file_mapping)
        throw std::runtime_error("OpenFileMappingW failed: bridge not found or access denied");
    state->header = static_cast<SharedHeader*>(MapViewOfFile(
        state->file_mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0));
    if (!state->header) throw std::runtime_error("MapViewOfFile failed");
    // Reject truncated or forged section views before dereferencing metadata.
    MEMORY_BASIC_INFORMATION mapped{};
    if (VirtualQuery(state->header, &mapped, sizeof(mapped)) != sizeof(mapped) ||
        mapped.State != MEM_COMMIT || mapped.RegionSize < sizeof(SharedHeader))
        throw std::runtime_error("PCM mapping is shorter than protocol header");

    auto* h = state->header;
    const auto role = static_cast<VirtualEndpointRole>(h->role);
    const VirtualBridgeFormat format{h->sample_rate,
        static_cast<std::uint16_t>(h->channels), h->capacity_frames};
    if (static_cast<std::uint32_t>(load32(
            reinterpret_cast<volatile LONG*>(&h->magic))) != bridge_magic ||
        h->version != bridge_version || role != expected_role ||
        h->slot != expected_slot || !valid_format(format) ||
        h->channels != format.channels ||
        mapped.RegionSize < size_bytes(format) || load32(&h->closed) ||
        !owner_alive(name))
        throw std::runtime_error("PCM mapping header, role, slot or owner lifetime invalid");
    state->pcm = reinterpret_cast<float*>(
        reinterpret_cast<std::byte*>(state->header) + sizeof(SharedHeader));
    // Header validation does not replace authorization or kernel ACL checks.
    return VirtualAudioBridge(std::move(state), name, role, expected_slot, format);
}

std::uint32_t VirtualAudioBridge::write(const float* interleaved,
                                        std::uint32_t frames) noexcept {
    if (!state_ || !state_->header || (!interleaved && frames) ||
        frames > (1u << 23))
        return 0;
    auto* h = state_->header;
    if (load32(&h->closed)) return 0;
    const auto w = static_cast<std::uint64_t>(load64(&h->written));
    const auto r = static_cast<std::uint64_t>(load64(&h->read));
    if (r > w || w - r > format_.capacity_frames) return 0; // fail closed
    const auto available = format_.capacity_frames -
                           static_cast<std::uint32_t>(w - r);
    const auto accepted = std::min(frames, available);
    const auto channels = format_.channels;
    const auto offset = static_cast<std::uint32_t>(w % format_.capacity_frames);
    const auto first = std::min(accepted, format_.capacity_frames - offset);
    if (first)
        std::memcpy(state_->pcm + static_cast<std::size_t>(offset) * channels,
                    interleaved, static_cast<std::size_t>(first) * channels * sizeof(float));
    if (accepted > first)
        std::memcpy(state_->pcm, interleaved + static_cast<std::size_t>(first) * channels,
                    static_cast<std::size_t>(accepted - first) * channels * sizeof(float));
    // InterlockedExchange64 is a full barrier: do not publish before PCM copy.
    InterlockedExchange64(&h->written, static_cast<LONG64>(w + accepted));
    if (accepted < frames)
        InterlockedExchangeAdd64(&h->overflow, frames - accepted);
    return accepted;
}

std::uint32_t VirtualAudioBridge::read(float* interleaved, std::uint32_t frames,
                                       bool pad_with_silence) noexcept {
    if (!state_ || !state_->header || (!interleaved && frames) ||
        frames > (1u << 23))
        return 0;
    auto* h = state_->header;
    const auto w = static_cast<std::uint64_t>(load64(&h->written));
    const auto r = static_cast<std::uint64_t>(load64(&h->read));
    if (r > w || w - r > format_.capacity_frames) return 0;
    const auto accepted = std::min(frames, static_cast<std::uint32_t>(w - r));
    const auto channels = format_.channels;
    const auto offset = static_cast<std::uint32_t>(r % format_.capacity_frames);
    const auto first = std::min(accepted, format_.capacity_frames - offset);
    if (first)
        std::memcpy(interleaved, state_->pcm + static_cast<std::size_t>(offset) * channels,
                    static_cast<std::size_t>(first) * channels * sizeof(float));
    if (accepted > first)
        std::memcpy(interleaved + static_cast<std::size_t>(first) * channels,
                    state_->pcm,
                    static_cast<std::size_t>(accepted - first) * channels * sizeof(float));
    InterlockedExchange64(&h->read, static_cast<LONG64>(r + accepted));
    if (accepted < frames) {
        InterlockedExchangeAdd64(&h->underflow, frames - accepted);
        if (pad_with_silence)
            std::memset(interleaved + static_cast<std::size_t>(accepted) * channels,
                        0, static_cast<std::size_t>(frames - accepted) * channels * sizeof(float));
    }
    return accepted;
}

VirtualBridgeCounters VirtualAudioBridge::counters() const noexcept {
    if (!state_ || !state_->header) return {};
    auto* h = state_->header;
    const auto w = static_cast<std::uint64_t>(load64(&h->written));
    const auto r = static_cast<std::uint64_t>(load64(&h->read));
    const auto queued = (w >= r && w - r <= format_.capacity_frames)
        ? static_cast<std::uint32_t>(w - r) : 0;
    return {w, r, static_cast<std::uint64_t>(load64(&h->overflow)),
            static_cast<std::uint64_t>(load64(&h->underflow)), queued,
            load32(&h->closed) != 0 || (!state_->owner && !owner_alive(name_))};
}

void VirtualAudioBridge::close() noexcept { state_.reset(); }

} // namespace auralweave

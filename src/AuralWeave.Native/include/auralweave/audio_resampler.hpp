#pragma once
#include <cstddef>
#include <cstdint>
#include <vector>

namespace auralweave {

/// Prototype streaming linear resampler with bounded output ring.
/// Supports mono/stereo Float32 internal PCM. Never allocates inside push/pop.
/// TODO: replace linear interpolation with anti-aliasing polyphase filter for release audio.
class StreamResampler final {
public:
    StreamResampler(std::uint32_t source_rate, std::uint16_t source_channels,
                    std::uint32_t output_rate, std::uint16_t output_channels,
                    std::uint32_t buffer_ms = 250);

    // Interleaved Float32 source frames, called in strict order.
    void push(const float* source, std::size_t frames) noexcept;
    // Pop up to 'frames' into destination; missing frames are silence.
    // Returns the count that was available, before zero-filling.
    std::size_t pop(float* destination, std::size_t frames) noexcept;
    [[nodiscard]] std::uint64_t overrun_frames() const noexcept { return overruns_; }
    [[nodiscard]] std::uint64_t underrun_frames() const noexcept { return underruns_; }
    [[nodiscard]] std::size_t buffered_frames() const noexcept { return length_; }

private:
    [[nodiscard]] float sample(const float* data, std::size_t frames,
                               std::int64_t offset, unsigned channel) const noexcept;
    void write_frame(float left, float right) noexcept;

    std::uint32_t source_rate_{};
    std::uint32_t output_rate_{};
    std::uint16_t source_channels_{};
    std::uint16_t output_channels_{};
    std::vector<float> ring_{};
    std::size_t capacity_{};
    std::size_t head_{};
    std::size_t length_{};
    std::uint64_t incoming_{};
    std::uint64_t overruns_{};
    std::uint64_t underruns_{};
    double next_position_{};
    float previous_left_{};
    float previous_right_{};
    bool has_previous_{};
};

} // namespace auralweave

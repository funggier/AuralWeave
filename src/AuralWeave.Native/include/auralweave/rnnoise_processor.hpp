#pragma once
#include "auralweave/audio_resampler.hpp"
#include "rnnoise.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>

namespace auralweave {

/// Per-route RNNoise denoiser. PCM input/output: interleaved float32 [-1,1].
/// Built-in Xiph RNNoise v0.1.1 processes exactly 480 samples/channel at 48 kHz.
/// Construct/prepare outside the real-time callback. process() does not allocate.
/// When bypassed, the audio engine MUST skip process() entirely.
class RnnoiseProcessor final {
public:
    RnnoiseProcessor(std::uint32_t sample_rate, std::uint16_t channels);
    ~RnnoiseProcessor();
    RnnoiseProcessor(const RnnoiseProcessor&) = delete;
    RnnoiseProcessor& operator=(const RnnoiseProcessor&) = delete;

    void reset() noexcept;
    void process(float* interleaved_audio, std::size_t frames) noexcept;
    [[nodiscard]] std::uint64_t processed_frames() const noexcept {
        return processed_frames_;
    }
    [[nodiscard]] std::uint64_t underflow_frames() const noexcept {
        return output_.underrun_frames();
    }
    [[nodiscard]] std::uint32_t added_latency_ms() const noexcept { return 10; }

private:
    struct StateDeleter {
        void operator()(DenoiseState* state) const noexcept;
    };
    using StatePtr = std::unique_ptr<DenoiseState, StateDeleter>;

    const std::uint16_t channels_;
    StreamResampler input_;
    StreamResampler output_;
    StatePtr states_[2];
    std::array<float, 480 * 2> frame_{};
    std::array<float, 480 * 2> denoised_{};
    std::array<float, 480> mono_input_{};
    std::array<float, 480> mono_output_{};
    std::uint64_t processed_frames_{};
};

} // namespace auralweave

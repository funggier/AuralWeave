#include "auralweave/rnnoise_processor.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <stdexcept>

namespace auralweave {

void RnnoiseProcessor::StateDeleter::operator()(DenoiseState* state) const noexcept {
    if (state) rnnoise_destroy(state);
}

RnnoiseProcessor::RnnoiseProcessor(std::uint32_t sample_rate,
                                   std::uint16_t channels)
    : channels_(channels),
      input_(sample_rate, channels, 48000, channels, 100),
      output_(48000, channels, sample_rate, channels, 100) {
    if (rnnoise_get_frame_size() != 480)
        throw std::runtime_error("Unexpected RNNoise frame size");
    for (std::uint16_t ch = 0; ch < channels_; ++ch) {
        states_[ch].reset(rnnoise_create(nullptr));
        if (!states_[ch]) throw std::runtime_error("RNNoise state initialization failed");
    }
    reset();
}

RnnoiseProcessor::~RnnoiseProcessor() = default;

void RnnoiseProcessor::reset() noexcept {
    input_.reset();
    output_.reset();
    processed_frames_ = 0;
    for (std::uint16_t ch = 0; ch < channels_; ++ch)
        rnnoise_init(states_[ch].get(), nullptr);

    // RNNoise is frame-based and must gather 10 ms before a complete result
    // exists. Pre-roll exactly 10 ms silence so arbitrary input callback
    // sizes can be returned without creating a dropout every 480 samples.
    std::array<float, (480 + 1) * 2> silence{};
    output_.push(silence.data(), 481);
}

void RnnoiseProcessor::process(float* audio, std::size_t frames) noexcept {
    if (!audio || frames == 0) return;
    input_.push(audio, frames);
    while (input_.buffered_frames() >= 480) {
        input_.pop(frame_.data(), 480);
        for (std::uint16_t ch = 0; ch < channels_; ++ch) {
            for (std::size_t i = 0; i < 480; ++i)
                mono_input_[i] = std::clamp(
                    frame_[i * channels_ + ch] * 32768.0F, -32768.0F, 32767.0F);
            rnnoise_process_frame(states_[ch].get(),
                                  mono_output_.data(), mono_input_.data());
            for (std::size_t i = 0; i < 480; ++i) {
                const float value = mono_output_[i] / 32768.0F;
                denoised_[i * channels_ + ch] =
                    std::isfinite(value) ? std::clamp(value, -1.0F, 1.0F) : 0.0F;
            }
        }
        output_.push(denoised_.data(), 480);
    }
    output_.pop(audio, frames);
    processed_frames_ += frames;
}

} // namespace auralweave

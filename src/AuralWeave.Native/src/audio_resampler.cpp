#include "auralweave/audio_resampler.hpp"
#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace auralweave {

StreamResampler::StreamResampler(std::uint32_t source_rate, std::uint16_t source_channels,
                                 std::uint32_t output_rate, std::uint16_t output_channels,
                                 std::uint32_t buffer_ms)
    : source_rate_(source_rate), output_rate_(output_rate),
      source_channels_(source_channels), output_channels_(output_channels) {
    if (source_rate < 8000 || source_rate > 384000 || output_rate < 8000 ||
        output_rate > 384000 || (source_channels != 1 && source_channels != 2) ||
        (output_channels != 1 && output_channels != 2) ||
        buffer_ms < 20 || buffer_ms > 1000)
        throw std::invalid_argument("Unsupported rate/channel/buffer configuration");
    capacity_ = std::max<std::size_t>(2, static_cast<std::size_t>(output_rate) * buffer_ms / 1000);
    ring_.resize(capacity_ * output_channels_);
}

void StreamResampler::reset() noexcept {
    std::fill(ring_.begin(), ring_.end(), 0.0F);
    head_ = 0;
    length_ = 0;
    incoming_ = 0;
    overruns_ = 0;
    underruns_ = 0;
    next_position_ = 0.0;
    previous_left_ = 0.0F;
    previous_right_ = 0.0F;
}
float StreamResampler::sample(const float* data, std::size_t frames,
                              std::int64_t offset, unsigned channel) const noexcept {
    if (offset < 0)
        return channel == 0 ? previous_left_ : previous_right_;
    if (static_cast<std::size_t>(offset) >= frames) return 0.0F;
    const std::size_t i = static_cast<std::size_t>(offset) * source_channels_;
    if (source_channels_ == 1) return data[i];
    return data[i + channel];
}

void StreamResampler::write_frame(float left, float right) noexcept {
    if (length_ == capacity_) {
        if (++head_ == capacity_) head_ = 0;
        --length_;
        ++overruns_;
    }
    auto at = head_ + length_;
    if (at >= capacity_) at -= capacity_;
    if (output_channels_ == 1)
        ring_[at] = std::clamp((left + right) * 0.5F, -1.0F, 1.0F);
    else {
        ring_[at * 2] = std::clamp(left, -1.0F, 1.0F);
        ring_[at * 2 + 1] = std::clamp(right, -1.0F, 1.0F);
    }
    ++length_;
}

void StreamResampler::push_matching_pcm(const float* data, std::size_t frames) noexcept {
    if (frames > capacity_) {
        const auto skipped = frames - capacity_;
        data += skipped * output_channels_;
        overruns_ += skipped;
        frames = capacity_;
    }
    const auto dropped = length_ - std::min(length_, capacity_ - frames);
    head_ = (head_ + dropped) % capacity_;
    length_ -= dropped;
    overruns_ += dropped;

    const auto tail = (head_ + length_) % capacity_;
    const auto first_samples = std::min(frames, capacity_ - tail) * output_channels_;
    const auto clamp_sample = [](float sample) { return std::clamp(sample, -1.0F, 1.0F); };
    std::transform(data, data + first_samples, ring_.data() + tail * output_channels_, clamp_sample);
    std::transform(data + first_samples, data + frames * output_channels_, ring_.data(), clamp_sample);
    length_ += frames;
}

void StreamResampler::push(const float* data, std::size_t frames) noexcept {
    if (!data || !frames) return;
    if (source_rate_ == output_rate_ && source_channels_ == output_channels_) {
        push_matching_pcm(data, frames);
        return;
    }
    const auto begin = static_cast<std::int64_t>(incoming_);
    const auto end = begin + static_cast<std::int64_t>(frames);
    const auto step = static_cast<double>(source_rate_) / static_cast<double>(output_rate_);
    // The final input frame needs one more source sample for interpolation;
    // it will be emitted with the first frame of the following block.
    while (next_position_ < static_cast<double>(end - 1)) {
        const auto lower = static_cast<std::int64_t>(std::floor(next_position_));
        if (lower < begin - 1) {
            next_position_ = static_cast<double>(begin - 1);
            continue;
        }
        const auto pos = lower - begin;
        const float fraction = static_cast<float>(next_position_ - static_cast<double>(lower));
        const float a_left = sample(data, frames, pos, 0);
        const float b_left = sample(data, frames, pos + 1, 0);
        const float a_right = source_channels_ == 2 ? sample(data, frames, pos, 1) : a_left;
        const float b_right = source_channels_ == 2 ? sample(data, frames, pos + 1, 1) : b_left;
        write_frame(a_left + (b_left - a_left) * fraction,
                    a_right + (b_right - a_right) * fraction);
        next_position_ += step;
    }
    previous_left_ = data[(frames - 1) * source_channels_];
    previous_right_ = source_channels_ == 2
        ? data[(frames - 1) * source_channels_ + 1] : previous_left_;
    incoming_ += frames;
}

std::size_t StreamResampler::pop(float* destination, std::size_t frames) noexcept {
    if (!destination || !frames) return 0;
    const auto take = std::min(frames, length_);
    const auto first = std::min(take, capacity_ - head_);
    std::copy_n(ring_.data() + head_ * output_channels_, first * output_channels_, destination);
    std::copy_n(ring_.data(), (take - first) * output_channels_, destination + first * output_channels_);
    std::fill(destination + take * output_channels_,
              destination + frames * output_channels_, 0.0F);
    head_ = (head_ + take) % capacity_;
    length_ -= take;
    underruns_ += frames - take;
    return take;
}

} // namespace auralweave

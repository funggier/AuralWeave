#include "auralweave/voice_filters.hpp"
#include <algorithm>
#include <cmath>
#include <numbers>

namespace auralweave {

Equalizer::Equalizer(std::uint32_t sample_rate, std::span<const double> frequencies,
                     std::span<const float> gains)
    : band_count_(frequencies.size()) {
    for (std::size_t band = 0; band < band_count_; ++band) {
        const auto frequency = frequencies[band];
        // A band beyond the source's Nyquist frequency cannot be realized.
        if (frequency >= sample_rate / 2.0) continue;

        BandKind kind = BandKind::peak;
        if (band == 0) kind = BandKind::low_shelf;
        else if (band == band_count_ - 1) kind = BandKind::high_shelf;
        bands_[band].configure(kind, frequency, sample_rate, gains[band]);
    }
}

void Equalizer::Band::configure(BandKind kind, double frequency,
                                        double rate, double db) noexcept {
    if (db == 0.0) return;
    active = true;

    // RBJ Audio EQ Cookbook, Q = 0.8 for the bell and S = 1 for the shelves.
    const double amplitude = std::pow(10.0, db / 40.0);
    const double omega = 2.0 * std::numbers::pi * frequency / rate;
    const double cosine = std::cos(omega);
    const double sine = std::sin(omega);
    double a0;
    if (kind == BandKind::peak) {
        const double alpha = sine / 1.6;
        a0 = 1.0 + alpha / amplitude;
        a1 = b1 = -2.0 * cosine;
        a2 = 1.0 - alpha / amplitude;
        b0 = 1.0 + alpha * amplitude;
        b2 = 1.0 - alpha * amplitude;
    } else {
        const double sum = amplitude + 1.0;
        const double difference = amplitude - 1.0;
        const double beta = std::sqrt(2.0 * amplitude) * sine;
        const double sum_cos = sum * cosine;
        const double diff_cos = difference * cosine;
        if (kind == BandKind::low_shelf) {
            b0 = amplitude * (sum - diff_cos + beta);
            b1 = 2.0 * amplitude * (difference - sum_cos);
            b2 = amplitude * (sum - diff_cos - beta);
            a0 = sum + diff_cos + beta;
            a1 = -2.0 * (difference + sum_cos);
            a2 = sum + diff_cos - beta;
        } else {
            b0 = amplitude * (sum + diff_cos + beta);
            b1 = -2.0 * amplitude * (difference + sum_cos);
            b2 = amplitude * (sum + diff_cos - beta);
            a0 = sum - diff_cos + beta;
            a1 = 2.0 * (difference - sum_cos);
            a2 = sum - diff_cos - beta;
        }
    }
    b0 /= a0;
    b1 /= a0;
    b2 /= a0;
    a1 /= a0;
    a2 /= a0;
}

void Equalizer::Band::process(float* samples, std::size_t frames,
                                      std::uint16_t channels) noexcept {
    if (!active) return;
    for (std::size_t i = 0; i < frames * channels; i += channels) {
        for (std::size_t ch = 0; ch < channels; ++ch) {
            const double input = samples[i + ch];
            const double output = b0 * input + z1[ch];
            z1[ch] = b1 * input - a1 * output + z2[ch];
            z2[ch] = b2 * input - a2 * output;
            samples[i + ch] = static_cast<float>(output);
        }
    }
    // Avoid gradual denormal slowdowns after a filter tail decays to silence.
    for (std::size_t ch = 0; ch < channels; ++ch) {
        if (std::fabs(z1[ch]) < 1e-30) z1[ch] = 0.0;
        if (std::fabs(z2[ch]) < 1e-30) z2[ch] = 0.0;
    }
}

void Equalizer::process(float* samples, std::size_t frames,
                                std::uint16_t channels) noexcept {
    for (std::size_t band = 0; band < band_count_; ++band)
        bands_[band].process(samples, frames, channels);
}

Compressor::Compressor(std::uint32_t sample_rate, float threshold_db, float ratio,
                       float attack_ms, float release_ms, float makeup_db) noexcept
    : threshold_(std::pow(10.0F, threshold_db / 20.0F)),
      exponent_(1.0F / ratio - 1.0F),
      attack_(std::exp(-1000.0F / (sample_rate * attack_ms))),
      release_(std::exp(-1000.0F / (sample_rate * release_ms))),
      makeup_(std::pow(10.0F, makeup_db / 20.0F)) {}

void Compressor::process(float* samples, std::size_t frames, std::uint16_t channels) noexcept {
    for (std::size_t i = 0; i < frames * channels; i += channels) {
        float peak = 0.0F;
        for (std::size_t ch = 0; ch < channels; ++ch)
            peak = std::max(peak, std::fabs(samples[i + ch]));

        const float target = peak > threshold_ ? std::pow(peak / threshold_, exponent_) : 1.0F;
        const float coefficient = target < gain_ ? attack_ : release_;
        gain_ = target + coefficient * (gain_ - target);
        const float applied_gain = gain_ * makeup_;
        for (std::size_t ch = 0; ch < channels; ++ch)
            samples[i + ch] *= applied_gain;
    }
}

} // namespace auralweave

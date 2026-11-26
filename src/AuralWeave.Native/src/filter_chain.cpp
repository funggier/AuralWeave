#include "auralweave/filter_chain.hpp"
#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace auralweave {
namespace {

std::span<const double> equalizer_frequencies(FilterKind kind) noexcept {
    static constexpr std::array three_band{160.0, 1000.0, 3500.0};
    static constexpr std::array five_band{100.0, 300.0, 1000.0, 3000.0, 8000.0};
    static constexpr std::array seven_band{80.0, 160.0, 400.0, 1000.0, 2500.0, 5000.0, 10000.0};
    switch (kind) {
        case FilterKind::equalizer: return three_band;
        case FilterKind::equalizer_5_band: return five_band;
        case FilterKind::equalizer_7_band: return seven_band;
        default: return {};
    }
}

} // namespace

bool valid_filter_spec(const FilterSpec& spec) noexcept {
    if (!std::isfinite(spec.parameter) ||
        !std::all_of(spec.additional.begin(), spec.additional.end(),
                     [](float value) { return std::isfinite(value); })) return false;
    const auto& [second, third, fourth, fifth, sixth, seventh] = spec.additional;
    switch (spec.kind) {
        case FilterKind::gain: return spec.parameter >= 0.0F && spec.parameter <= 2.0F;
        case FilterKind::rnnoise: return spec.parameter == 0.0F;
        case FilterKind::noise_gate: return spec.parameter >= -80.0F && spec.parameter <= 0.0F;
        case FilterKind::limiter: return spec.parameter >= -24.0F && spec.parameter <= 0.0F;
        case FilterKind::equalizer:
        case FilterKind::equalizer_5_band:
        case FilterKind::equalizer_7_band: {
            const auto band_count = equalizer_frequencies(spec.kind).size();
            return std::fabs(spec.parameter) <= 12.0F &&
                std::ranges::all_of(std::span{spec.additional}.first(band_count - 1),
                                    [](float db) { return std::fabs(db) <= 12.0F; });
        }
        case FilterKind::compressor:
            return spec.parameter >= -60.0F && spec.parameter <= 0.0F &&
                second >= 1.0F && second <= 20.0F && third >= 1.0F && third <= 100.0F &&
                fourth >= 10.0F && fourth <= 1000.0F && fifth >= 0.0F && fifth <= 24.0F;
        default: return false;
    }
}

FilterChain::FilterChain(std::uint32_t sample_rate, std::uint16_t channels,
                         const std::vector<FilterSpec>& specs)
    : sample_rate_(sample_rate), channels_(channels) {
    if (sample_rate < 8000 || sample_rate > 384000 || (channels != 1 && channels != 2))
        throw std::invalid_argument("Invalid DSP input format");
    if (specs.size() > 64) throw std::invalid_argument("Too many filters");
    stages_.reserve(specs.size());
    for (const auto& spec : specs) {
        if (!valid_filter_spec(spec)) throw std::invalid_argument("Invalid filter configuration");
        stages_.emplace_back(spec);
        if (!spec.enabled) continue;

        auto& stage = stages_.back();
        const auto& [second, third, fourth, fifth, sixth, seventh] = spec.additional;
        switch (spec.kind) {
            case FilterKind::rnnoise:
                stage.noise = std::make_unique<RnnoiseProcessor>(sample_rate, channels);
                break;
            case FilterKind::equalizer:
            case FilterKind::equalizer_5_band:
            case FilterKind::equalizer_7_band: {
                const auto frequencies = equalizer_frequencies(spec.kind);
                const std::array gains{spec.parameter, second, third, fourth, fifth, sixth, seventh};
                const auto active_gains = std::span{gains}.first(frequencies.size());
                if (std::ranges::any_of(active_gains, [](float db) { return db != 0.0F; }))
                    stage.equalizer = std::make_unique<Equalizer>(sample_rate, frequencies, active_gains);
                break;
            }
            case FilterKind::compressor:
                if (second != 1.0F || fifth != 0.0F)
                    stage.compressor = std::make_unique<Compressor>(
                        sample_rate, spec.parameter, second, third, fourth, fifth);
                break;
            default: break;
        }
    }
}

void FilterChain::process(float* samples, std::size_t frames) noexcept {
    if (!samples || !frames || stages_.empty()) return;
    for (auto& stage : stages_) {
        if (!stage.spec.enabled) {
            stage.previously_enabled = false;
            continue;
        }
        const float parameter = stage.spec.parameter;
        switch (stage.spec.kind) {
            case FilterKind::gain:
                for (std::size_t i = 0; i < frames * channels_; ++i)
                    samples[i] = std::clamp(samples[i] * parameter, -1.0F, 1.0F);
                break;
            case FilterKind::rnnoise:
                if (!stage.previously_enabled) stage.noise->reset();
                stage.noise->process(samples, frames);
                break;
            case FilterKind::noise_gate: {
                // Envelope gate, fixed 5ms attack and 80ms release.
                const float threshold = std::pow(10.0F, parameter / 20.0F);
                const float attack = std::exp(-1.0F / (sample_rate_ * 0.005F));
                const float release = std::exp(-1.0F / (sample_rate_ * 0.080F));
                for (std::size_t i = 0; i < frames; ++i) {
                    float peak = 0.0F;
                    for (std::size_t ch = 0; ch < channels_; ++ch)
                        peak = std::max(peak, std::fabs(samples[i * channels_ + ch]));
                    const float target = peak >= threshold ? 1.0F : 0.0F;
                    const float coef = target > stage.envelope ? attack : release;
                    stage.envelope = target + coef * (stage.envelope - target);
                    for (std::size_t ch = 0; ch < channels_; ++ch)
                        samples[i * channels_ + ch] *= stage.envelope;
                }
                break;
            }
            case FilterKind::limiter: {
                // Zero-lookahead peak ceiling limiter. Immediate peak protection
                // and 100ms gain recovery; can color fast transients.
                const float ceiling = std::pow(10.0F, parameter / 20.0F);
                const float release = std::exp(-1.0F / (sample_rate_ * 0.100F));
                for (std::size_t i = 0; i < frames; ++i) {
                    float peak = 0.0F;
                    for (std::size_t ch = 0; ch < channels_; ++ch)
                        peak = std::max(peak, std::fabs(samples[i * channels_ + ch]));
                    const float required = peak > ceiling ? ceiling / peak : 1.0F;
                    stage.smooth_gain = required < stage.smooth_gain ? required :
                        (1.0F + release * (stage.smooth_gain - 1.0F));
                    for (std::size_t ch = 0; ch < channels_; ++ch) {
                        float& value = samples[i * channels_ + ch];
                        value = std::clamp(value * stage.smooth_gain, -ceiling, ceiling);
                    }
                }
                break;
            }
            case FilterKind::equalizer:
            case FilterKind::equalizer_5_band:
            case FilterKind::equalizer_7_band:
                if (stage.equalizer) stage.equalizer->process(samples, frames, channels_);
                break;
            case FilterKind::compressor:
                if (stage.compressor) stage.compressor->process(samples, frames, channels_);
                break;
        }
        stage.previously_enabled = true;
    }
}

} // namespace auralweave

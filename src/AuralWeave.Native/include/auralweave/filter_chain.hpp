#pragma once
#include "auralweave/rnnoise_processor.hpp"
#include "auralweave/voice_filters.hpp"
#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <vector>

namespace auralweave {

// The ABI values intentionally map to the C# FilterKind enum.
// Blank/unselected slots are OMITTED from the native graph.
enum class FilterKind : int {
    gain = 1, rnnoise = 2, noise_gate = 3, limiter = 4, equalizer = 5,
    compressor = 6, equalizer_5_band = 8, equalizer_7_band = 9
};

struct FilterSpec {
    FilterKind kind{};
    float parameter{};
    bool enabled{true};
    std::array<float, 6> additional{};
};

/// Construct this immutable-topology DSP graph off the real-time worker.
/// Only the audio worker mutates DSP state. Published graphs
/// are replaced atomically at packet boundaries.
class FilterChain final {
public:
    FilterChain(std::uint32_t sample_rate, std::uint16_t channels,
                const std::vector<FilterSpec>& specs);
    void process(float* samples, std::size_t frames) noexcept;
    [[nodiscard]] bool empty() const noexcept { return stages_.empty(); }
    [[nodiscard]] std::size_t size() const noexcept { return stages_.size(); }
private:
    struct Stage {
        explicit Stage(FilterSpec spec) : spec(spec) {}
        FilterSpec spec;
        std::unique_ptr<RnnoiseProcessor> noise;
        std::unique_ptr<Equalizer> equalizer;
        std::unique_ptr<Compressor> compressor;
        float envelope{1.0F};
        float smooth_gain{1.0F};
        bool previously_enabled{};
    };
    std::uint32_t sample_rate_{};
    std::uint16_t channels_{};
    std::vector<Stage> stages_;
};

bool valid_filter_spec(const FilterSpec& spec) noexcept;

} // namespace auralweave

#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

namespace auralweave {

class Equalizer final {
public:
    Equalizer(std::uint32_t sample_rate, std::span<const double> frequencies,
              std::span<const float> gains);
    void process(float* samples, std::size_t frames, std::uint16_t channels) noexcept;

private:
    enum class BandKind { low_shelf, peak, high_shelf };

    struct Band {
        double b0{1}, b1{}, b2{}, a1{}, a2{};
        std::array<double, 2> z1{}, z2{};
        bool active{};

        void configure(BandKind kind, double frequency, double rate, double db) noexcept;
        void process(float* samples, std::size_t frames, std::uint16_t channels) noexcept;
    };
    std::array<Band, 7> bands_{};
    std::size_t band_count_{};
};

class Compressor final {
public:
    Compressor(std::uint32_t sample_rate, float threshold_db, float ratio,
               float attack_ms, float release_ms, float makeup_db) noexcept;
    void process(float* samples, std::size_t frames, std::uint16_t channels) noexcept;

private:
    float threshold_, exponent_, attack_, release_, makeup_;
    float gain_{1.0F};
};

} // namespace auralweave

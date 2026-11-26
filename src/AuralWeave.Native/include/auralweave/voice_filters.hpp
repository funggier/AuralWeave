#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>

namespace auralweave {

class ThreeBandEqualizer final {
public:
    ThreeBandEqualizer(std::uint32_t sample_rate, float low_db, float mid_db, float high_db);
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
    std::array<Band, 3> bands_{};
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

class PitchShifter final {
public:
    PitchShifter(std::uint32_t sample_rate, std::uint16_t channels, float semitones);
    ~PitchShifter();
    void process(float* samples, std::size_t frames) noexcept;

private:
    struct State;
    std::unique_ptr<State> state_;
};

} // namespace auralweave

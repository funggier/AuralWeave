#include "auralweave/route_graph.hpp"
#include "auralweave/audio_resampler.hpp"
#include "auralweave/rnnoise_processor.hpp"
#include "auralweave/filter_chain.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <iostream>
#include <limits>
#include <numbers>
#include <stdexcept>
#include <string_view>
#include <vector>

using namespace auralweave;

static void expect(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
    std::cout << "PASS: " << message << "\n";
}

static double tone_level(const std::vector<float>& signal, unsigned rate, double frequency) {
    double real = 0.0, imaginary = 0.0;
    const auto start = signal.size() / 2;
    for (std::size_t i = start; i < signal.size(); ++i) {
        const auto angle = 2.0 * std::numbers::pi * frequency * i / rate;
        real += signal[i] * std::cos(angle);
        imaginary += signal[i] * std::sin(angle);
    }
    return 2.0 * std::hypot(real, imaginary) / (signal.size() - start);
}

static std::vector<float> tone(unsigned rate, double frequency) {
    std::vector<float> samples(rate / 2);
    for (std::size_t i = 0; i < samples.size(); ++i)
        samples[i] = 0.1F * static_cast<float>(std::sin(2.0 * std::numbers::pi * frequency * i / rate));
    return samples;
}

static void check_voice_filters() {
    const std::array<float, 4> input{.13F, -.27F, .8F, -.45F};
    auto bypassed = input;
    FilterChain bypass(48000, 2, {
        {FilterKind::equalizer, 0.0F, true},
        {FilterKind::pitch_shift, 0.0F, true},
        {FilterKind::pitch_shift, 12.0F, false}
    });
    bypass.process(bypassed.data(), 2);
    expect(bypassed == input, "flat EQ and zero or bypassed Pitch Shift preserve exact samples");

    for (const auto [frequency, spec] : std::array{
         std::pair{40.0, FilterSpec{FilterKind::equalizer, 6.0F, true}},
         std::pair{1000.0, FilterSpec{FilterKind::equalizer, 0.0F, true, {-6.0F}}},
         std::pair{14000.0, FilterSpec{FilterKind::equalizer, 0.0F, true, {0.0F, 6.0F}}}}) {
        auto samples = tone(48000, frequency);
        FilterChain equalizer(48000, 1, {spec});
        equalizer.process(samples.data(), samples.size());
        const auto expected = frequency == 1000.0 ? 0.05 : 0.2;
        expect(std::fabs(tone_level(samples, 48000, frequency) - expected) < 0.005,
               "EQ shelf/bell gain agrees with requested dB");
    }

    const FilterSpec comp{FilterKind::compressor, -20.0F, true, {4.0F, 5.0F, 80.0F, 6.0F}};
    FilterChain compressor(48000, 2, {comp});
    std::vector<float> peaks(48000, 0.5F);
    for (std::size_t i = 1; i < peaks.size(); i += 2) peaks[i] = 0.05F;
    compressor.process(peaks.data(), peaks.size() / 2);
    const auto expected = 0.1 * std::pow(5.0, 0.25) * std::pow(10.0, 6.0 / 20.0);
    expect(std::fabs(peaks[peaks.size() - 2] - expected) < 0.001 &&
           std::fabs(peaks.back() * 10.0 - peaks[peaks.size() - 2]) < 0.001 &&
           peaks.front() > peaks[peaks.size() - 2],
           "Compressor applies attack, ratio and makeup gain with linked stereo");
    std::fill(peaks.begin(), peaks.end(), 0.01F);
    compressor.process(peaks.data(), peaks.size() / 2);
    expect(peaks.back() > peaks.front() && peaks.back() > .019F, "Compressor releases gain on quiet input");
    expect(!valid_filter_spec(FilterSpec{FilterKind::compressor, -20.0F, true, {0, 5, 80, 0}}) &&
           !valid_filter_spec(FilterSpec{FilterKind::equalizer, 0.0F, true,
                                        {std::numeric_limits<float>::quiet_NaN()}}),
           "native rejects invalid secondary filter parameters");

    for (auto rate : {48000U, 192000U}) {
        for (auto semitones : {-12.0F, 12.0F}) {
            auto samples = tone(rate, 440.0);
            FilterChain pitch(rate, 1, {{FilterKind::pitch_shift, semitones, true}});
            std::size_t position = 0;
            while (position < samples.size()) {
                const auto count = std::min<std::size_t>(rate / 100 - position % 37, samples.size() - position);
                pitch.process(samples.data() + position, count);
                position += count;
            }
            const auto target = semitones > 0 ? 880.0 : 220.0;
            const auto shifted_level = tone_level(samples, rate, target);
            expect(std::all_of(samples.begin(), samples.end(), [](float v) { return std::isfinite(v); }) &&
                   shifted_level > 0.01 && shifted_level > 5.0 * tone_level(samples, rate, 440.0),
                   "Pitch Shift transposes a tone by one octave across uneven route packets");
        }
    }
}

int wmain(int argc, wchar_t* argv[]) {
    try {
        RouteGraph graph;
        expect(graph.add_output(Output{1, L"Speakers", 1.0F, false}), "add physical output");
        expect(!graph.add_output(Output{2, L"Speakers", 1.0F, false}), "reject duplicate output");
        expect(graph.add_output(Output{3, L"Headphones", 1.0F, true}), "add second output");
        expect(graph.remove_output(1) && !graph.has_output(L"Speakers"), "removed output available");
        expect(graph.add_effect(Effect{100, EffectKind::gain, true, 0.5F}), "add Gain instance 1");
        expect(graph.add_effect(Effect{101, EffectKind::gain, true, 0.5F}), "add Gain instance 2");
        expect(!graph.add_effect(Effect{101, EffectKind::gain, true, 0.5F}), "reject duplicate effect ID");
        std::array<float, 4> samples{1.0F, -1.0F, 0.25F, -0.25F};
        graph.process(samples.data(), 2, 2);
        expect(std::fabs(samples[0] - .25F) < .00001F, "repeated effects processed in order");
        expect(graph.move_effect(101, 0) && graph.effects().front().instance_id == 101,
               "reorder effect instances");
        expect(graph.remove_effect(100), "remove individual effect");

        expect(valid_filter_spec(FilterSpec{FilterKind::gain, 1.0F, true}),
               "valid native Gain filter");
        expect(!valid_filter_spec(FilterSpec{FilterKind::limiter, -30.0F, true}),
               "reject invalid limiter ceiling");
        FilterChain direct(48000, 2, {});
        std::array<float, 4> direct_pcm{0.3F, -0.4F, 0.8F, -0.9F};
        direct.process(direct_pcm.data(), 2);
        expect(direct.empty() && direct_pcm[0] == 0.3F && direct_pcm[3] == -0.9F,
               "zero filters preserves PCM unchanged");

        FilterChain gain_twice(48000, 1, {
            {FilterKind::gain, 0.5F, true},
            {FilterKind::gain, 0.5F, true}
        });
        std::array<float, 2> twice{0.8F, -0.8F};
        gain_twice.process(twice.data(), twice.size());
        expect(std::fabs(twice[0] - 0.2F) < 0.0001F,
               "two independent Gain instances process sequentially");

        FilterChain limiter_gain(48000, 1, {
            {FilterKind::limiter, -6.0F, true},
            {FilterKind::gain, 0.5F, true}
        });
        FilterChain gain_limiter(48000, 1, {
            {FilterKind::gain, 0.5F, true},
            {FilterKind::limiter, -6.0F, true}
        });
        float a = 1.0F, b = 1.0F;
        limiter_gain.process(&a, 1);
        gain_limiter.process(&b, 1);
        expect(a < b - 0.1F, "real DSP filter order changes output");

        FilterChain gate(48000, 1, {{FilterKind::noise_gate, -45.0F, true}});
        std::vector<float> soft(4800, 0.00001F);
        gate.process(soft.data(), soft.size());
        expect(soft.back() < 0.000007F,
               "noise gate attenuates below-threshold signal");

        FilterChain repeated_noise(48000, 1, {
            {FilterKind::rnnoise, 0.0F, true},
            {FilterKind::rnnoise, 0.0F, true},
            {FilterKind::gain, 1.0F, true}
        });
        std::vector<float> noise_frame(480 * 2, 0.01F);
        repeated_noise.process(noise_frame.data(), 960);
        expect(repeated_noise.size() == 3 &&
               std::all_of(noise_frame.begin(), noise_frame.end(),
                   [](float f){ return std::isfinite(f) && std::fabs(f) <= 1.0F; }),
               "RNNoise may repeat as separately owned ordered filter instances");
        check_voice_filters();

        StreamResampler down(192000, 2, 48000, 2);
        std::vector<float> down_input(1920 * 2, .25F);
        down.push(down_input.data(), 1920);
        std::vector<float> down_output(480 * 2, 0);
        expect(down.pop(down_output.data(), 480) == 480, "resample 192k to 48k frame count");
        expect(std::fabs(down_output[0] - .25F) < .00001F &&
               down.overrun_frames() == 0, "downsample passes steady signal");

        StreamResampler up(48000, 1, 192000, 2);
        std::vector<float> up_input(480, 0.5F);
        up.push(up_input.data(), 480);
        std::vector<float> up_output(1916 * 2);
        expect(up.pop(up_output.data(), 1916) == 1916, "resample 48k to 192k frame count");
        expect(std::fabs(up_output[0] - .5F) < .00001F &&
               std::fabs(up_output[1] - .5F) < .00001F,
               "mono input maps to stereo output");

        StreamResampler bounded(48000, 2, 48000, 2, 20);
        std::vector<float> flood(4800 * 2, .2F);
        bounded.push(flood.data(), 4800);
        expect(bounded.overrun_frames() > 0 && bounded.buffered_frames() <= 960,
               "resampler ring is bounded and reports overrun");
        std::vector<float> large_read(2000 * 2);
        bounded.pop(large_read.data(), 2000);
        expect(bounded.underrun_frames() > 0, "resampler reports underrun");

        // RNNoise 48-kHz/480-sample standard model in a streaming DSP wrapper:
        // tests exercise internal states without opening any audio devices.
        for (auto rate : {44100U, 48000U, 96000U, 192000U}) {
            for (std::uint16_t channels : {std::uint16_t{1}, std::uint16_t{2}}) {
                RnnoiseProcessor noise(rate, channels);
                const auto frames_per_call = static_cast<std::size_t>(rate / 100);
                std::vector<float> block(frames_per_call * channels, 0.0F);
                for (int iteration = 0; iteration < 20; ++iteration) {
                    for (std::size_t i = 0; i < block.size(); ++i)
                        block[i] = 0.1F * std::sin(static_cast<float>(i + iteration * block.size()) * .15F);
                    noise.process(block.data(), frames_per_call);
                    for (float sample : block)
                        if (!std::isfinite(sample) || std::fabs(sample) > 1.0001F)
                            throw std::runtime_error("RNNoise returned non-finite or out-of-range PCM");
                }
                expect(noise.processed_frames() == 20 * frames_per_call,
                       "RNNoise streaming finite PCM across mono/stereo sample rates");
                noise.reset();
                expect(noise.processed_frames() == 0,
                       "RNNoise processor state resets safely");
            }
        }
        if (argc > 1 && std::wstring_view(argv[1]) == L"--devices") {
            auto capture = enumerate_endpoints(true);
            auto render = enumerate_endpoints(false);
            std::wcout << L"Native capture endpoints: " << capture.size() << L"\n";
            for (const auto& endpoint : capture)
                std::wcout << L"  " << endpoint.name <<
                    (endpoint.is_default ? L" [default]" : L"") << L"\n";
            std::wcout << L"Native render endpoints: " << render.size() << L"\n";
            for (const auto& endpoint : render)
                std::wcout << L"  " << endpoint.name <<
                    (endpoint.is_default ? L" [default]" : L"") << L"\n";
        }
        return 0;
    } catch (const std::exception& ex) {
        std::cerr << "FAIL: " << ex.what() << "\n";
        return 1;
    }
}

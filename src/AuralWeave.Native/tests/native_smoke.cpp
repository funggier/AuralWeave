#include "auralweave/route_graph.hpp"
#include "auralweave/audio_resampler.hpp"
#include "auralweave/rnnoise_processor.hpp"
#include "auralweave/filter_chain.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string_view>
#include <vector>

using namespace auralweave;

static void expect(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
    std::cout << "PASS: " << message << "\n";
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

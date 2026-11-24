#include "auralweave/route_graph.hpp"
#include "auralweave/audio_resampler.hpp"

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

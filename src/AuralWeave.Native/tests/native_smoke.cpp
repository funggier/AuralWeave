#include "auralweave/route_graph.hpp"

#include <array>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string_view>

using namespace auralweave;

static void expect(bool condition, const char* description) {
    if (!condition) throw std::runtime_error(description);
    std::cout << "PASS: " << description << "\n";
}

int wmain(int argc, wchar_t* argv[]) {
    try {
        RouteGraph graph;
        expect(graph.add_output(Output{1, L"Speakers", 1.0F, false}), "add physical output");
        expect(!graph.add_output(Output{2, L"Speakers", 1.0F, false}), "reject same output in route");
        expect(graph.add_output(Output{3, L"Headphones", 1.0F, true}), "add second physical output");
        expect(graph.remove_output(1) && !graph.has_output(L"Speakers"), "deleted output available again");

        expect(graph.add_effect(Effect{100, EffectKind::gain, true, 0.5F}), "add Gain instance 1");
        expect(graph.add_effect(Effect{101, EffectKind::gain, true, 0.5F}), "add repeated Gain instance 2");
        expect(!graph.add_effect(Effect{101, EffectKind::gain, true, 0.5F}),
               "reject duplicated instance IDs");
        std::array<float, 4> samples{1.0F, -1.0F, 0.25F, -0.25F};
        graph.process(samples.data(), 2, 2);
        expect(std::fabs(samples[0] - .25F) < 0.00001F, "two independent gains processed in sequence");
        expect(graph.move_effect(101, 0) && graph.effects().front().instance_id == 101,
               "reorder independent instances");
        expect(graph.remove_effect(100), "delete individual instance");

        if (argc > 1 && std::wstring_view(argv[1]) == L"--devices") {
            auto capture = enumerate_endpoints(true);
            auto render = enumerate_endpoints(false);
            std::wcout << L"Native WASAPI capture endpoints: " << capture.size() << L"\n";
            for (const auto& endpoint : capture)
                std::wcout << L"  " << endpoint.name << (endpoint.is_default ? L" [default]" : L"") << L"\n";
            std::wcout << L"Native WASAPI render endpoints: " << render.size() << L"\n";
            for (const auto& endpoint : render)
                std::wcout << L"  " << endpoint.name << (endpoint.is_default ? L" [default]" : L"") << L"\n";
        }
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << "\n";
        return 1;
    }
}

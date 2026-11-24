#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace auralweave {

struct Output {
    std::uint64_t instance_id{};
    std::wstring endpoint_id{};
    float gain{1.0F};
    bool muted{};
};

enum class EffectKind : std::uint8_t {
    gain
};

struct Effect {
    std::uint64_t instance_id{};
    EffectKind kind{EffectKind::gain};
    bool enabled{true};
    float gain{1.0F};
};

/// Configuration only: the audio device capture/render engine remains a separate backend.
/// Use prepare/swap snapshots outside real-time callbacks when integrating this graph.
class RouteGraph {
public:
    [[nodiscard]] bool add_output(Output output);
    [[nodiscard]] bool remove_output(std::uint64_t instance_id);
    [[nodiscard]] bool add_effect(Effect effect);
    [[nodiscard]] bool remove_effect(std::uint64_t instance_id);
    [[nodiscard]] bool move_effect(std::uint64_t instance_id, std::size_t index);

    [[nodiscard]] bool has_output(std::wstring_view endpoint_id) const noexcept;
    [[nodiscard]] const std::vector<Output>& outputs() const noexcept { return outputs_; }
    [[nodiscard]] const std::vector<Effect>& effects() const noexcept { return effects_; }

    /// Processes float32 interleaved PCM, -1..+1. No allocation occurs in process().
    /// Repeated EffectKind values are allowed; each effect needs a unique instance_id.
    void process(float* pcm, std::size_t frames, std::size_t channels) const noexcept;

private:
    std::vector<Output> outputs_;
    std::vector<Effect> effects_;
};

struct AudioDevice {
    std::wstring id;
    std::wstring name;
    bool is_default{};
};

[[nodiscard]] std::vector<AudioDevice> enumerate_endpoints(bool capture);

} // namespace auralweave

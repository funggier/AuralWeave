#include "auralweave/route_graph.hpp"

#include <algorithm>
#include <cmath>
#include <utility>

namespace auralweave {

bool RouteGraph::has_output(std::wstring_view endpoint_id) const noexcept {
    return std::any_of(outputs_.begin(), outputs_.end(),
        [endpoint_id](const Output& item) { return item.endpoint_id == endpoint_id; });
}

bool RouteGraph::add_output(Output output) {
    if (output.instance_id == 0 || output.endpoint_id.empty() ||
        !std::isfinite(output.gain) || output.gain < 0.0F || output.gain > 2.0F ||
        has_output(output.endpoint_id) ||
        std::any_of(outputs_.begin(), outputs_.end(),
            [&output](const Output& value) { return value.instance_id == output.instance_id; }))
        return false;

    outputs_.push_back(std::move(output));
    return true;
}

bool RouteGraph::remove_output(std::uint64_t instance_id) {
    const auto it = std::find_if(outputs_.begin(), outputs_.end(),
        [instance_id](const Output& output) { return output.instance_id == instance_id; });
    if (it == outputs_.end()) return false;
    outputs_.erase(it);
    return true;
}

bool RouteGraph::add_effect(Effect effect) {
    if (effect.instance_id == 0 || !std::isfinite(effect.gain) ||
        effect.gain < 0.0F || effect.gain > 2.0F ||
        std::any_of(effects_.begin(), effects_.end(),
            [&effect](const Effect& existing) { return existing.instance_id == effect.instance_id; }))
        return false;

    effects_.push_back(effect);
    return true;
}

bool RouteGraph::remove_effect(std::uint64_t instance_id) {
    const auto it = std::find_if(effects_.begin(), effects_.end(),
        [instance_id](const Effect& effect) { return effect.instance_id == instance_id; });
    if (it == effects_.end()) return false;
    effects_.erase(it);
    return true;
}

bool RouteGraph::move_effect(std::uint64_t instance_id, std::size_t index) {
    if (index >= effects_.size()) return false;
    const auto it = std::find_if(effects_.begin(), effects_.end(),
        [instance_id](const Effect& effect) { return effect.instance_id == instance_id; });
    if (it == effects_.end()) return false;
    const auto from = static_cast<std::size_t>(it - effects_.begin());
    const auto effect = *it;
    effects_.erase(effects_.begin() + static_cast<std::ptrdiff_t>(from));
    effects_.insert(effects_.begin() + static_cast<std::ptrdiff_t>(index), effect);
    return true;
}

void RouteGraph::process(float* pcm, std::size_t frames, std::size_t channels) const noexcept {
    if (!pcm || !frames || !channels) return;
    if (frames > static_cast<std::size_t>(-1) / channels) return;
    const auto count = frames * channels;
    for (const auto& effect : effects_) {
        if (!effect.enabled || effect.kind != EffectKind::gain || effect.gain == 1.0F)
            continue;
        for (std::size_t sample = 0; sample < count; ++sample)
            pcm[sample] = std::clamp(pcm[sample] * effect.gain, -1.0F, 1.0F);
    }
}

} // namespace auralweave

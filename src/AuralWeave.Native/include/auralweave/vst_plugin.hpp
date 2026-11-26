#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>

namespace auralweave {

class VstPlugin {
public:
    virtual ~VstPlugin() = default;
    virtual void configure(int sample_rate) = 0;
    [[nodiscard]] virtual int sample_rate() const noexcept = 0;
    [[nodiscard]] virtual bool has_editor() const noexcept = 0;
    virtual void show_editor(void* owner) = 0;
    virtual void process(float* pcm, std::size_t frames, std::uint16_t channels) noexcept = 0;

    // Close on the owning UI thread before releasing its handle. The audio
    // graph may retain a reference, but must see a bypassed processor.
    virtual void close() noexcept = 0;
};

using VstPluginHandle = std::shared_ptr<VstPlugin>;
VstPluginHandle load_vst3(const wchar_t* path, int sample_rate);

} // namespace auralweave

#pragma once

#include "auralweave/vst_plugin.hpp"

namespace auralweave {

class Vst2Plugin final : public VstPlugin {
public:
    static std::shared_ptr<Vst2Plugin> load(const wchar_t* path, int sample_rate);
    ~Vst2Plugin() override;

    void configure(int sample_rate) override;
    [[nodiscard]] int sample_rate() const noexcept override;
    [[nodiscard]] bool has_editor() const noexcept override;
    void show_editor(void* owner) override;
    void process(float* samples, std::size_t frames, std::uint16_t channels) noexcept override;

    // Called by the owning UI before releasing its handle, even when a
    // published audio graph still holds a shared reference.
    void close() noexcept override;

private:
    Vst2Plugin();
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace auralweave

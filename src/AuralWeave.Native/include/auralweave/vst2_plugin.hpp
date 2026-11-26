#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>

namespace auralweave {

class Vst2Plugin final {
public:
    static std::shared_ptr<Vst2Plugin> load(const wchar_t* path, int sample_rate);
    ~Vst2Plugin();

    void configure(int sample_rate);
    [[nodiscard]] int sample_rate() const noexcept;
    [[nodiscard]] bool has_editor() const noexcept;
    void show_editor(void* owner);
    void process(float* samples, std::size_t frames, std::uint16_t channels) noexcept;

    // Called by the owning UI before releasing its handle, even when a
    // published audio graph still holds a shared reference.
    void close() noexcept;

private:
    Vst2Plugin();
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// Handle owned by the control plane. Audio graphs acquire their own references.
using Vst2Handle = std::shared_ptr<Vst2Plugin>;

} // namespace auralweave

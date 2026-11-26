#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "auralweave/vst2_plugin.hpp"
#include "auralweave/audio_engine_api.h"
#include "vst2_abi.hpp"
#include "vst_editor_window.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <filesystem>
#include <mutex>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

namespace auralweave {

struct Vst2Plugin::Impl {
    static constexpr int block_size = 1024;
    static constexpr int max_pins = 32;
    static constexpr UINT resize_editor_message = WM_APP + 1;
    static constexpr wchar_t editor_class[] = L"AuralWeaveVst2Editor";

    std::mutex control;
    HMODULE module{};
    vst2::Effect* effect{};
    std::wstring title;
    std::atomic<int> rate{48000};
    std::atomic<std::uint64_t> position{};
    std::atomic<HWND> editor{};
    VstEditorWindow editor_window;
    int input_count{}, output_count{};
    bool active{}, editor_open{}, editor_available{};
    std::vector<float> input, output;
    std::array<float*, max_pins> input_pins{}, output_pins{};

    // Realtime callbacks stay on the calling thread. An editor may also call
    // back from its own Win32 message handler outside a host dispatcher call.
    inline static thread_local Impl* current{};
    inline static thread_local bool in_process{};
    inline static std::mutex instances_mutex;
    inline static std::unordered_map<vst2::Effect*, Impl*> instances;
    struct Context {
        Impl* previous = current;
        bool prior_process = in_process;
        Context(Impl* plugin, bool audio = false) noexcept {
            current = plugin;
            in_process = audio;
        }
        ~Context() {
            current = previous;
            in_process = prior_process;
        }
    };

    ~Impl() { shutdown(); }

    std::intptr_t dispatch(vst2::EffectOp op, std::intptr_t value = 0,
                           void* ptr = nullptr, float arg = 0.0F) const {
        Context context(const_cast<Impl*>(this));
        return effect->dispatcher(effect, op, 0, value, ptr, arg);
    }

    static std::intptr_t __cdecl callback(vst2::Effect* fx, std::int32_t op,
                                          std::int32_t index, std::intptr_t value,
                                          void* ptr, float) noexcept {
        // These replies can be requested before VSTPluginMain returns.
        switch (op) {
            case vst2::host_version: return 2400;
            case vst2::get_block_size: return block_size;
            case vst2::get_vendor_name:
            case vst2::get_product_name:
                if (!ptr) return 0;
                std::memcpy(ptr, "AuralWeave", sizeof("AuralWeave"));
                return 1;
            case vst2::get_vendor_version: return 1;
            case vst2::get_language: return 1; // English
            case vst2::get_process_level: return in_process ? 2 : 1;
            case vst2::get_automation_state: return 1; // automation off
            case vst2::can_do:
                if (!ptr) return 0;
                return (std::strcmp(static_cast<const char*>(ptr), "sizeWindow") == 0 ||
                        std::strcmp(static_cast<const char*>(ptr), "sendVstTimeInfo") == 0) ? 1 : 0;
            case vst2::get_sample_rate:
            case vst2::get_time:
            case vst2::size_window:
                break;
            default: return 0;
        }

        if (current) return current->reply(op, index, value);
        std::lock_guard guard(instances_mutex);
        const auto found = instances.find(fx);
        return found == instances.end() ? 0 : found->second->reply(op, index, value);
    }

    std::intptr_t reply(std::int32_t op, std::int32_t index, std::intptr_t value) const noexcept {
        if (op == vst2::get_sample_rate) return rate.load();
        if (op == vst2::size_window) {
            const auto window = editor.load();
            if (!window || index <= 0 || index > 4096 || value <= 0 || value > 4096) return 0;
            return PostMessageW(window, resize_editor_message, static_cast<WPARAM>(index), value) ? 1 : 0;
        }

        thread_local vst2::TimeInfo time;
        time.sample_position = static_cast<double>(position.load());
        time.sample_rate = rate.load();
        time.tempo = 120.0;
        time.quarter_notes = time.sample_position * 2.0 / time.sample_rate;
        time.bar_start = std::floor(time.quarter_notes / 4.0) * 4.0;
        time.time_signature_top = time.time_signature_bottom = 4;
        time.flags = vst2::quarter_notes_valid | vst2::tempo_valid |
                     vst2::bars_valid | vst2::time_signature_valid;
        if (in_process) time.flags |= vst2::transport_playing;
        return reinterpret_cast<std::intptr_t>(&time);
    }

    void load(const wchar_t* path, int sample_rate) {
        if (!path || !std::filesystem::path(path).is_absolute())
            throw std::invalid_argument("Enter an absolute path to a 64-bit VST2 DLL");
        if (sample_rate < 8000 || sample_rate > 384000)
            throw std::invalid_argument("Unsupported VST audio sample rate");
        rate.store(sample_rate);
        title = std::filesystem::path(path).stem().wstring();
        module = LoadLibraryExW(path, nullptr,
                                LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module)
            throw std::runtime_error("Windows could not load this DLL (error " +
                                     std::to_string(GetLastError()) + ")");
        // Function pointer conversion is required by GetProcAddress.
#pragma warning(suppress: 4191)
        const auto entry = reinterpret_cast<vst2::Entry>(GetProcAddress(module, "VSTPluginMain"));
        if (!entry) throw std::runtime_error("Missing the VSTPluginMain export");
        {
            Context context(this);
            effect = entry(callback);
        }
        if (!effect || effect->magic != vst2::effect_magic || !effect->dispatcher) {
            effect = nullptr;
            throw std::runtime_error("The DLL did not return a VST2 effect");
        }
        {
            std::lock_guard guard(instances_mutex);
            instances.emplace(effect, this);
        }
        dispatch(vst2::open);
        if ((effect->flags & vst2::is_synth) || !(effect->flags & vst2::can_replace) ||
            !effect->process_replacing || effect->inputs < 1 || effect->inputs > max_pins ||
            effect->outputs < 1 || effect->outputs > max_pins)
            throw std::runtime_error("Only VST2 effects with audio input and float output are supported");

        input_count = effect->inputs;
        output_count = effect->outputs;
        input.resize(static_cast<std::size_t>(input_count) * block_size);
        output.resize(static_cast<std::size_t>(output_count) * block_size);
        for (int pin = 0; pin < input_count; ++pin)
            input_pins[pin] = input.data() + pin * block_size;
        for (int pin = 0; pin < output_count; ++pin)
            output_pins[pin] = output.data() + pin * block_size;
        editor_available = (effect->flags & vst2::has_editor) != 0;
        configure(sample_rate);
    }

    void configure(int sample_rate) {
        if (sample_rate < 8000 || sample_rate > 384000)
            throw std::invalid_argument("Unsupported VST audio sample rate");
        std::lock_guard guard(control);
        if (!effect) throw std::runtime_error("The VST plugin is closed");
        if (active && rate.load() == sample_rate) return;
        deactivate();
        rate.store(sample_rate);
        position.store(0);
        dispatch(vst2::set_sample_rate, 0, nullptr, static_cast<float>(sample_rate));
        dispatch(vst2::set_block_size, block_size);
        dispatch(vst2::set_active, 1);
        active = true;
        dispatch(vst2::start_process);
    }

    void deactivate() {
        if (!active) return;
        dispatch(vst2::stop_process);
        dispatch(vst2::set_active, 0);
        active = false;
    }

    void process(float* pcm, std::size_t frames, std::uint16_t channels) noexcept {
        std::unique_lock guard(control, std::try_to_lock);
        if (!guard || !active || !pcm || (channels != 1 && channels != 2) ||
            effect->inputs != input_count || effect->outputs != output_count) return;
        try {
            Context context(this, true);
            while (frames) {
                const auto count = static_cast<int>(std::min<std::size_t>(frames, block_size));
                for (int frame = 0; frame < count; ++frame) {
                    const auto offset = static_cast<std::size_t>(frame) * channels;
                    const float left = pcm[offset];
                    const float right = channels == 2 ? pcm[offset + 1] : left;
                    input_pins[0][frame] = input_count == 1 ? (left + right) * .5F : left;
                    if (input_count > 1) input_pins[1][frame] = right;
                }
                for (int pin = 2; pin < input_count; ++pin) std::fill_n(input_pins[pin], count, 0.0F);
                for (int pin = 0; pin < output_count; ++pin) std::fill_n(output_pins[pin], count, 0.0F);

                effect->process_replacing(effect, input_pins.data(), output_pins.data(), count);
                position.fetch_add(static_cast<std::uint64_t>(count));
                for (int frame = 0; frame < count; ++frame) {
                    const auto offset = static_cast<std::size_t>(frame) * channels;
                    const float left = output_pins[0][frame];
                    const float right = output_count > 1 ? output_pins[1][frame] : left;
                    const float safe_left = std::isfinite(left) ? left : 0.0F;
                    const float safe_right = std::isfinite(right) ? right : 0.0F;
                    pcm[offset] = channels == 1 ? (safe_left + safe_right) * .5F : safe_left;
                    if (channels == 2) pcm[offset + 1] = safe_right;
                }
                pcm += static_cast<std::size_t>(count) * channels;
                frames -= count;
            }
        } catch (...) {
            // Keep the remaining source samples intact if a plugin throws.
        }
    }

    void fit_editor() {
        vst2::EditorRect* rect{};
        dispatch(vst2::edit_rectangle, 0, &rect);
        if (!rect) return;
        const auto width = rect->right - rect->left;
        const auto height = rect->bottom - rect->top;
        if (width > 0 && width <= 4096 && height > 0 && height <= 4096)
            editor_window.resize(width, height);
    }

    static LRESULT editor_message(void* context, HWND window, UINT message, WPARAM param, LPARAM data) noexcept {
        auto& plugin = *static_cast<Impl*>(context);
        try {
            switch (message) {
                case WM_CLOSE: {
                    std::lock_guard guard(plugin.control);
                    plugin.destroy_editor();
                    return 0;
                }
                case WM_TIMER: {
                    std::unique_lock guard(plugin.control, std::try_to_lock);
                    if (guard && plugin.editor_open) plugin.dispatch(vst2::edit_idle);
                    return 0;
                }
                case resize_editor_message:
                    // The callback checks the size before posting to the editor thread.
                    plugin.editor_window.resize(static_cast<int>(param), static_cast<int>(data));
                    return 0;
            }
        } catch (...) {
            return 0;
        }
        return DefWindowProcW(window, message, param, data);
    }

    void show_editor(HWND owner) {
        std::lock_guard guard(control);
        if (!effect || !editor_available) throw std::runtime_error("This plugin has no editor");
        if (editor_window.window()) {
            editor_window.show();
            return;
        }

        editor_window.create(owner, editor_class, title.c_str(), editor_message, this);
        const auto window = editor_window.window();
        editor.store(window);
        try {
            fit_editor();
            editor_open = true;
            dispatch(vst2::edit_open, 0, editor_window.content());
            fit_editor();
            SetTimer(window, 1, 33, nullptr);
            editor_window.show();
        } catch (...) {
            destroy_editor();
            throw;
        }
    }

    void destroy_editor() noexcept {
        const auto window = editor.exchange(nullptr);
        if (!window) return;
        KillTimer(window, 1);
        if (editor_open) {
            editor_open = false;
            try { dispatch(vst2::edit_close); } catch (...) {}
        }
        editor_window.destroy();
    }

    void shutdown() noexcept {
        std::lock_guard guard(control);
        destroy_editor();
        if (effect) {
            try { deactivate(); } catch (...) {}
            auto* fx = effect;
            try { dispatch(vst2::close); } catch (...) {}
            effect = nullptr;
            active = false;
            std::lock_guard registration(instances_mutex);
            instances.erase(fx);
        }
        if (module) {
            FreeLibrary(module);
            module = nullptr;
        }
    }
};

Vst2Plugin::Vst2Plugin() : impl_(std::make_unique<Impl>()) {}
Vst2Plugin::~Vst2Plugin() = default;

std::shared_ptr<Vst2Plugin> Vst2Plugin::load(const wchar_t* path, int sample_rate) {
    auto plugin = std::shared_ptr<Vst2Plugin>(new Vst2Plugin());
    plugin->impl_->load(path, sample_rate);
    return plugin;
}

void Vst2Plugin::configure(int sample_rate) { impl_->configure(sample_rate); }
int Vst2Plugin::sample_rate() const noexcept { return impl_->rate.load(); }
bool Vst2Plugin::has_editor() const noexcept { return impl_->editor_available; }
void Vst2Plugin::show_editor(void* owner) { impl_->show_editor(static_cast<HWND>(owner)); }
void Vst2Plugin::close() noexcept { impl_->shutdown(); }

void Vst2Plugin::process(float* samples, std::size_t frames, std::uint16_t channels) noexcept {
    impl_->process(samples, frames, channels);
}

} // namespace auralweave

namespace {

void vst_error(wchar_t* error, std::size_t size, const char* text) noexcept {
    if (!error || !size) return;
    size -= 1;
    std::size_t i = 0;
    for (; i < size && text[i]; ++i) error[i] = static_cast<unsigned char>(text[i]);
    error[i] = L'\0';
}

} // namespace

AW_API void* aw_vst2_load(const wchar_t* path, int sample_rate,
                          wchar_t* error, std::size_t size) noexcept {
    try { return new auralweave::VstPluginHandle(auralweave::Vst2Plugin::load(path, sample_rate)); }
    catch (const std::exception& e) { vst_error(error, size, e.what()); }
    catch (...) { vst_error(error, size, "VST2 plugin initialization failed"); }
    return nullptr;
}

AW_API void* aw_vst3_load(const wchar_t* path, int sample_rate,
                         wchar_t* error, std::size_t size) noexcept {
    try { return new auralweave::VstPluginHandle(auralweave::load_vst3(path, sample_rate)); }
    catch (const std::exception& e) { vst_error(error, size, e.what()); }
    catch (...) { vst_error(error, size, "VST3 module initialization failed"); }
    return nullptr;
}

AW_API void aw_vst2_close(void* handle) noexcept {
    const std::unique_ptr<auralweave::VstPluginHandle> plugin(static_cast<auralweave::VstPluginHandle*>(handle));
    if (plugin) (*plugin)->close();
}

AW_API int aw_vst2_configure(void* handle, int sample_rate,
                            wchar_t* error, std::size_t size) noexcept {
    if (!handle) return 0;
    try {
        (*static_cast<auralweave::VstPluginHandle*>(handle))->configure(sample_rate);
        return 1;
    } catch (const std::exception& e) { vst_error(error, size, e.what()); }
    catch (...) { vst_error(error, size, "Could not configure VST2 plugin"); }
    return 0;
}

AW_API int aw_vst2_has_editor(void* handle) noexcept {
    return handle && (*static_cast<auralweave::VstPluginHandle*>(handle))->has_editor() ? 1 : 0;
}

AW_API int aw_vst2_show_editor(void* handle, void* owner,
                              wchar_t* error, std::size_t size) noexcept {
    if (!handle) return 0;
    try {
        (*static_cast<auralweave::VstPluginHandle*>(handle))->show_editor(owner);
        return 1;
    } catch (const std::exception& e) { vst_error(error, size, e.what()); }
    catch (...) { vst_error(error, size, "Could not show VST2 plugin editor"); }
    return 0;
}

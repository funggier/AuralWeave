#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <ksmedia.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <memory>
#include <mutex>
#include <set>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "auralweave/audio_engine_api.h"
#include "auralweave/audio_resampler.hpp"
#include "auralweave/rnnoise_processor.hpp"
#include "auralweave/filter_chain.hpp"

using Microsoft::WRL::ComPtr;

namespace {

void checked(HRESULT result, const char* action) {
    if (FAILED(result)) {
        char hex[32]{};
        sprintf_s(hex, " (HRESULT 0x%08X)", static_cast<unsigned>(result));
        throw std::runtime_error(std::string(action) + hex);
    }
}

class Event final {
public:
    explicit Event(bool manual_reset = false) :
        handle_(CreateEventW(nullptr, manual_reset ? TRUE : FALSE, FALSE, nullptr)) {
        if (!handle_) throw std::runtime_error("CreateEventW failed");
    }
    ~Event() { CloseHandle(handle_); }
    Event(const Event&) = delete;
    Event& operator=(const Event&) = delete;
    HANDLE get() const noexcept { return handle_; }
    void signal() const noexcept { SetEvent(handle_); }
    void reset() const noexcept { ResetEvent(handle_); }
private:
    HANDLE handle_{};
};

struct MixFormat final {
    WAVEFORMATEX* data{};
    ~MixFormat() { if (data) CoTaskMemFree(data); }
    MixFormat() = default;
    MixFormat(const MixFormat&) = delete;
    MixFormat& operator=(const MixFormat&) = delete;
};

struct FormatInfo {
    std::uint32_t rate{};
    std::uint16_t channels{};
    bool is_float{};
};

FormatInfo format_info(const WAVEFORMATEX* f) {
    if (!f) throw std::runtime_error("No shared mix format");
    bool is_float = f->wFormatTag == WAVE_FORMAT_IEEE_FLOAT;
    bool is_pcm = f->wFormatTag == WAVE_FORMAT_PCM;
    if (f->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
        if (f->cbSize < sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX))
            throw std::runtime_error("Invalid extended mix format");
        const auto* ext = reinterpret_cast<const WAVEFORMATEXTENSIBLE*>(f);
        is_float = IsEqualGUID(ext->SubFormat, KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
        is_pcm = IsEqualGUID(ext->SubFormat, KSDATAFORMAT_SUBTYPE_PCM);
    }
    if ((f->nChannels != 1 && f->nChannels != 2) ||
        !((is_float && f->wBitsPerSample == 32) ||
          (is_pcm && f->wBitsPerSample == 16)) ||
        f->nSamplesPerSec < 8000 || f->nSamplesPerSec > 384000)
        throw std::runtime_error(
            "Native backend supports only mono/stereo Float32 or PCM16 endpoint mix formats");
    return FormatInfo{f->nSamplesPerSec, f->nChannels, is_float};
}

void init_client(IAudioClient* client, WAVEFORMATEX* format, DWORD flags) {
    // Windows Audio Engine MixFormat is a supported shared-mode format.
    checked(client->Initialize(AUDCLNT_SHAREMODE_SHARED, flags,
                               400000, 0, format, nullptr),
            "IAudioClient::Initialize");
}

struct RenderSink final {
    std::wstring endpoint;
    float gain{1.0F};
    bool muted{true};
    ComPtr<IAudioClient> client;
    ComPtr<IAudioRenderClient> writer;
    UINT32 buffer_frames{};
    FormatInfo format{};
    std::unique_ptr<auralweave::StreamResampler> resampler;
    std::unique_ptr<Event> render_event;
    std::vector<float> scratch;
};

class NativeRoute final {
public:
    NativeRoute() : stop_(true) {}
    ~NativeRoute() { stop(); }

    bool start(const wchar_t* source, const wchar_t* const* endpoint_ids,
               const float* gains, const int* muted, int count) {
        if (!source || !endpoint_ids || !gains || !muted || count <= 0 || count > 32) {
            fail(L"Invalid route configuration");
            return false;
        }
        stop();
        const std::wstring source_name(source);
        if (!source_name.starts_with(L"capture:") &&
            !source_name.starts_with(L"loopback:")) {
            fail(L"Source must be prefixed capture: or loopback:");
            return false;
        }
        std::set<std::wstring> used;
        std::vector<RenderSink> sinks;
        sinks.reserve(static_cast<std::size_t>(count));
        for (int i = 0; i < count; ++i) {
            if (!endpoint_ids[i] || !*endpoint_ids[i] ||
                !std::isfinite(gains[i]) || gains[i] < 0.0F || gains[i] > 2.0F ||
                !used.insert(endpoint_ids[i]).second) {
                fail(L"Invalid output gain or duplicate output endpoint");
                return false;
            }
            RenderSink item{};
            item.endpoint = endpoint_ids[i];
            item.gain = gains[i];
            item.muted = muted[i] != 0;
            sinks.push_back(std::move(item));
        }
        if (source_name.starts_with(L"loopback:") &&
            used.contains(source_name.substr(9))) {
            fail(L"Cannot loop back into the same playback endpoint");
            return false;
        }
        {
            std::lock_guard<std::mutex> lock(mutex_);
            source_ = source_name;
            sinks_ = std::move(sinks);
            effects_.clear();
            error_.clear();
        }
        sample_rate_.store(0);
        channels_.store(0);
        ordered_chain_.store(nullptr);
        frames_.store(0);
        overruns_.store(0);
        underruns_.store(0);
        peak_.store(0);
        stop_event();
        state_.store(1);
        try {
            worker_ = std::thread([this] { run(); });
        } catch (...) {
            fail(L"Cannot create audio thread");
            return false;
        }
        for (int i = 0; i < 300 && state_.load() == 1; ++i)
            Sleep(10);
        if (state_.load() != 2) {
            if (state_.load() == 1) fail(L"Timed out opening WASAPI endpoints");
            join(false);
            return false;
        }
        return true;
    }

    void stop() noexcept { join(true); }

    int state() const noexcept { return state_.load(); }
    std::uint64_t frames() const noexcept { return frames_.load(); }
    std::uint64_t overruns() const noexcept { return overruns_.load(); }
    std::uint64_t underruns() const noexcept { return underruns_.load(); }
    bool set_filter_chain(const int* kinds, const float* parameters,
                          const int* enabled, int count) {
        if (count < 0 || count > 64 || (count > 0 &&
            (!kinds || !parameters || !enabled))) return false;
        std::vector<auralweave::FilterSpec> specs;
        specs.reserve(static_cast<std::size_t>(count));
        for (int i = 0; i < count; ++i) {
            const auto spec = auralweave::FilterSpec{
                static_cast<auralweave::FilterKind>(kinds[i]),
                parameters[i], enabled[i] != 0
            };
            if (!auralweave::valid_filter_spec(spec)) return false;
            specs.push_back(spec);
        }
        // Build off the render thread, then atomically publish an immutable
        // ordered graph. If not started yet, the stream builds it on start.
        std::lock_guard<std::mutex> guard(mutex_);
        std::shared_ptr<auralweave::FilterChain> prepared;
        const auto sample_rate = sample_rate_.load();
        const auto channels = channels_.load();
        if (sample_rate && channels)
            prepared = std::make_shared<auralweave::FilterChain>(
                sample_rate, channels, specs);
        filter_specs_ = std::move(specs);
        ordered_chain_enabled_.store(true);
        if (prepared) ordered_chain_.store(prepared);
        return true;
    }
    bool set_noise_mode(int mode) noexcept {
        if (mode < 0 || mode > 1) return false;
        noise_mode_.store(mode);
        return true;
    }
    float peak() const noexcept { return peak_.load(); }

    std::wstring error() {
        std::lock_guard<std::mutex> lock(mutex_);
        return error_;
    }

    bool set_effects(const float* gains, const int* on, int count) {
        if (count < 0 || count > 256 || (count > 0 && (!gains || !on)))
            return false;
        std::vector<float> updated;
        updated.reserve(static_cast<std::size_t>(count));
        for (int i = 0; i < count; ++i) {
            if (!std::isfinite(gains[i]) || gains[i] < 0.0F || gains[i] > 2.0F)
                return false;
            updated.push_back(on[i] ? gains[i] : 1.0F);
        }
        std::lock_guard<std::mutex> lock(mutex_);
        effects_ = std::move(updated);
        return true;
    }

    bool set_output(int index, float gain, int muted) {
        if (index < 0 || !std::isfinite(gain) || gain < 0 || gain > 2)
            return false;
        std::lock_guard<std::mutex> lock(mutex_);
        if (static_cast<std::size_t>(index) >= sinks_.size()) return false;
        sinks_[static_cast<std::size_t>(index)].gain = gain;
        sinks_[static_cast<std::size_t>(index)].muted = muted != 0;
        return true;
    }

private:
    Event stop_{true};
    std::thread worker_;
    std::mutex mutex_;
    std::wstring source_;
    std::wstring error_;
    std::vector<RenderSink> sinks_;
    std::vector<float> effects_;
    std::vector<auralweave::FilterSpec> filter_specs_;
    std::atomic<std::shared_ptr<auralweave::FilterChain>> ordered_chain_;
    std::atomic<bool> ordered_chain_enabled_{false};
    std::atomic<std::uint32_t> sample_rate_{0};
    std::atomic<std::uint16_t> channels_{0};
    std::atomic<int> state_{0};  // 0=off, 1=starting, 2=running, 3=error
    std::atomic<std::uint64_t> frames_{0};
    std::atomic<std::uint64_t> overruns_{0};
    std::atomic<std::uint64_t> underruns_{0};
    std::atomic<float> peak_{0};
    std::atomic<int> noise_mode_{0}; // Default bypass preserves the existing fast path.

    void stop_event() noexcept { stop_.reset(); }
    void fail(const wchar_t* message) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            error_ = message;
        }
        state_.store(3);
    }
    void join(bool mark_stopped) noexcept {
        stop_.signal();
        if (worker_.joinable()) worker_.join();
        if (mark_stopped) {
            sample_rate_.store(0);
            channels_.store(0);
            ordered_chain_.store(nullptr);
            state_.store(0);
        }
    }

    void run() noexcept {
        const HRESULT init = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(init)) {
            fail(L"Native audio worker failed COM initialization");
            return;
        }
        try {
            stream();
        } catch (const std::exception& ex) {
            std::wstring message;
            for (unsigned char c : std::string(ex.what()))
                message.push_back(static_cast<wchar_t>(c));
            fail(message.c_str());
        } catch (...) {
            fail(L"Unexpected native audio worker exception");
        }
        // Dispose COM-owned objects BEFORE leaving the COM apartment.
        for (auto& sink : sinks_) {
            sink.writer.Reset();
            sink.client.Reset();
            sink.resampler.reset();
        }
        if (state_.load() != 3) state_.store(0);
        CoUninitialize();
    }

    static void copy_capture(std::vector<float>& dst, const BYTE* bytes,
                             std::size_t frames, FormatInfo fmt, bool silent) {
        const auto count = frames * fmt.channels;
        dst.resize(count);
        if (silent || !bytes)
            std::fill(dst.begin(), dst.end(), 0.0F);
        else if (fmt.is_float) {
            std::memcpy(dst.data(), bytes, count * sizeof(float));
            for (float& value : dst)
                if (!std::isfinite(value)) value = 0.0F;
        }
        else {
            const auto* input = reinterpret_cast<const std::int16_t*>(bytes);
            for (std::size_t i = 0; i < count; ++i)
                dst[i] = static_cast<float>(input[i]) / 32768.0F;
        }
    }

    static void convert_render(BYTE* dst, const float* src, std::size_t count,
                               bool output_float, float gain) {
        if (output_float) {
            auto* output = reinterpret_cast<float*>(dst);
            for (std::size_t i = 0; i < count; ++i)
                output[i] = std::clamp(src[i] * gain, -1.0F, 1.0F);
        } else {
            auto* output = reinterpret_cast<std::int16_t*>(dst);
            for (std::size_t i = 0; i < count; ++i) {
                const float value = std::clamp(src[i] * gain, -1.0F, 1.0F);
                output[i] = static_cast<std::int16_t>(std::clamp(
                    std::lround(value * 32768.0F), -32768L, 32767L));
            }
        }
    }

    void stream() {
        ComPtr<IMMDeviceEnumerator> enumerator;
        checked(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
                                 IID_PPV_ARGS(&enumerator)), "CoCreateInstance MMDeviceEnumerator");

        const bool loopback = source_.starts_with(L"loopback:");
        const std::wstring endpoint = source_.substr(loopback ? 9 : 8);
        ComPtr<IMMDevice> input_device;
        checked(enumerator->GetDevice(endpoint.c_str(), &input_device), "Get capture device");
        ComPtr<IAudioClient> capture;
        checked(input_device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr,
                                       &capture), "Activate capture IAudioClient");
        MixFormat capture_mix;
        checked(capture->GetMixFormat(&capture_mix.data), "Get capture mix format");
        const FormatInfo in_format = format_info(capture_mix.data);
        {
            std::lock_guard<std::mutex> guard(mutex_);
            sample_rate_.store(in_format.rate);
            channels_.store(in_format.channels);
            if (ordered_chain_enabled_.load()) {
                ordered_chain_.store(std::make_shared<auralweave::FilterChain>(
                    in_format.rate, in_format.channels, filter_specs_));
            }
        }
        // Prepare RNNoise before starting the capture/render streams. No model
        // allocation in the audio packet callback. Bypass skips process().
        auralweave::RnnoiseProcessor noise(in_format.rate, in_format.channels);
        bool noise_was_enabled = false;
        Event capture_event;
        init_client(capture.Get(), capture_mix.data,
                    AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
                        (loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0));
        checked(capture->SetEventHandle(capture_event.get()), "Set capture event");
        ComPtr<IAudioCaptureClient> reader;
        checked(capture->GetService(IID_PPV_ARGS(&reader)), "Get capture client");

        for (auto& sink : sinks_) {
            ComPtr<IMMDevice> out_device;
            checked(enumerator->GetDevice(sink.endpoint.c_str(), &out_device),
                    "Get render device");
            checked(out_device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr,
                                         &sink.client), "Activate render IAudioClient");
            MixFormat mix;
            checked(sink.client->GetMixFormat(&mix.data), "Get render mix format");
            sink.format = format_info(mix.data);
            sink.resampler = std::make_unique<auralweave::StreamResampler>(
                in_format.rate, in_format.channels,
                sink.format.rate, sink.format.channels, 80);
            init_client(sink.client.Get(), mix.data, AUDCLNT_STREAMFLAGS_EVENTCALLBACK);
            sink.render_event = std::make_unique<Event>();
            checked(sink.client->SetEventHandle(sink.render_event->get()), "Set render event");
            checked(sink.client->GetBufferSize(&sink.buffer_frames), "Get render buffer size");
            checked(sink.client->GetService(IID_PPV_ARGS(&sink.writer)),
                    "Get render client");
            sink.scratch.resize(static_cast<std::size_t>(sink.buffer_frames) *
                                sink.format.channels);
        }

        for (auto& sink : sinks_) {
            const UINT32 preroll = sink.buffer_frames / 4;
            if (preroll) {
                BYTE* data = nullptr;
                checked(sink.writer->GetBuffer(preroll, &data), "Render silent preroll");
                checked(sink.writer->ReleaseBuffer(preroll, AUDCLNT_BUFFERFLAGS_SILENT),
                        "Release silent preroll");
            }
            checked(sink.client->Start(), "Start render");
        }
        checked(capture->Start(), "Start capture");
        state_.store(2);

        // Windows' capture and each render endpoint run on independent clocks.
        // Service render events independently of capture callbacks: the old code
        // rendered only when capture arrived and could intermittently starve outputs.
        std::vector<HANDLE> events{stop_.get(), capture_event.get()};
        for (const auto& sink : sinks_) events.push_back(sink.render_event->get());

        std::vector<float> samples;
        samples.reserve(65536);
        std::array<float, 256> effects_snapshot{};

        auto capture_available = [&]() {
            for (;;) {
                UINT32 pending{};
                checked(reader->GetNextPacketSize(&pending), "GetNextPacketSize");
                if (!pending) break;
                BYTE* input{};
                UINT32 frame_count{};
                DWORD flags{};
                checked(reader->GetBuffer(&input, &frame_count, &flags, nullptr, nullptr),
                        "Capture GetBuffer");
                const auto sample_count = static_cast<std::size_t>(frame_count) * in_format.channels;
                if (sample_count > 65536) {
                    reader->ReleaseBuffer(frame_count);
                    throw std::runtime_error("Capture packet too large");
                }
                copy_capture(samples, input, frame_count, in_format,
                             (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0);
                checked(reader->ReleaseBuffer(frame_count), "Capture ReleaseBuffer");
                frames_.fetch_add(frame_count);

                float maximum = 0.0F;
                for (float value : samples)
                    if (std::isfinite(value)) maximum = std::max(maximum, std::fabs(value));
                peak_.store(std::min(maximum, 1.0F));

                // Ordered graph is the new product contract. No selected
                // filters => zero DSP processing and no denoiser buffering.
                // Old two-stage API remains only for compatibility tests.
                if (ordered_chain_enabled_.load()) {
                    auto chain = ordered_chain_.load();
                    if (chain && !chain->empty())
                        chain->process(samples.data(), frame_count);
                } else {
                    const bool enable_noise = noise_mode_.load() == 1;
                    if (enable_noise) {
                        if (!noise_was_enabled) noise.reset();
                        noise.process(samples.data(), frame_count);
                    }
                    noise_was_enabled = enable_noise;

                    std::size_t active_effects{};
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        active_effects = effects_.size();
                        std::copy(effects_.begin(), effects_.end(), effects_snapshot.begin());
                    }
                    for (std::size_t e = 0; e < active_effects; ++e) {
                        const float gain = effects_snapshot[e];
                        if (gain == 1.0F) continue;
                        for (float& value : samples)
                            value = std::clamp(value * gain, -1.0F, 1.0F);
                    }
                }
                // Distribute once-processed PCM into an independent bounded
                // converter/FIFO per destination; rendering is event-driven.
                for (auto& sink : sinks_) {
                    const auto previous = sink.resampler->overrun_frames();
                    sink.resampler->push(samples.data(), frame_count);
                    overruns_.fetch_add(sink.resampler->overrun_frames() - previous);
                }
            }
        };

        auto render_ready = [&](RenderSink& sink) {
            UINT32 padding{};
            checked(sink.client->GetCurrentPadding(&padding), "Get render padding");
            const UINT32 free_frames = sink.buffer_frames > padding
                ? sink.buffer_frames - padding : 0;
            // Keep only one render period in flight per callback, rather than
            // filling a large render buffer with stale frames or silence.
            const UINT32 period = std::max<UINT32>(1, sink.format.rate / 100);
            const UINT32 block = std::min(free_frames, period);
            if (!block) return;

            const auto previous = sink.resampler->underrun_frames();
            sink.resampler->pop(sink.scratch.data(), block);
            underruns_.fetch_add(sink.resampler->underrun_frames() - previous);

            float gain{};
            bool mute{};
            {
                std::lock_guard<std::mutex> guard(mutex_);
                gain = sink.gain;
                mute = sink.muted;
            }
            BYTE* output{};
            checked(sink.writer->GetBuffer(block, &output), "Render GetBuffer");
            if (!mute) convert_render(output, sink.scratch.data(),
                static_cast<std::size_t>(block) * sink.format.channels,
                sink.format.is_float, gain);
            checked(sink.writer->ReleaseBuffer(block,
                        mute ? AUDCLNT_BUFFERFLAGS_SILENT : 0),
                    "Render ReleaseBuffer");
        };

        for (;;) {
            const DWORD result = WaitForMultipleObjects(
                static_cast<DWORD>(events.size()), events.data(), FALSE, 150);
            if (result == WAIT_OBJECT_0) break;
            if (result == WAIT_FAILED) throw std::runtime_error("WASAPI event wait failed");
            if (result == WAIT_TIMEOUT) continue;
            if (result < WAIT_OBJECT_0 || result >= WAIT_OBJECT_0 + events.size())
                throw std::runtime_error("Unexpected WASAPI event index");

            const std::size_t event_index = static_cast<std::size_t>(result - WAIT_OBJECT_0);
            if (event_index == 1) {
                capture_available();
            } else if (event_index >= 2) {
                render_ready(sinks_[event_index - 2]);
            }
        }
        capture->Stop();
        for (auto& sink : sinks_) sink.client->Stop();
    }
};

} // namespace

AW_API void* aw_create() noexcept {
    try { return new NativeRoute(); } catch (...) { return nullptr; }
}
AW_API void aw_destroy(void* handle) noexcept { delete static_cast<NativeRoute*>(handle); }
AW_API int aw_start(void* handle, const wchar_t* source_id,
                    const wchar_t* const* endpoint_ids,
                    const float* gains, const int* muted, int count) noexcept {
    if (!handle) return 0;
    try { return static_cast<NativeRoute*>(handle)->start(
        source_id, endpoint_ids, gains, muted, count) ? 1 : 0; }
    catch (...) { return 0; }
}
AW_API void aw_stop(void* handle) noexcept {
    if (handle) static_cast<NativeRoute*>(handle)->stop();
}
AW_API int aw_state(void* handle) noexcept {
    return handle ? static_cast<NativeRoute*>(handle)->state() : 0;
}
AW_API std::uint64_t aw_captured_frames(void* handle) noexcept {
    return handle ? static_cast<NativeRoute*>(handle)->frames() : 0;
}
AW_API std::uint64_t aw_dropped_render_frames(void* handle) noexcept {
    return handle ? static_cast<NativeRoute*>(handle)->overruns() : 0;
}
AW_API std::uint64_t aw_underrun_render_frames(void* handle) noexcept {
    return handle ? static_cast<NativeRoute*>(handle)->underruns() : 0;
}
AW_API float aw_input_peak(void* handle) noexcept {
    return handle ? static_cast<NativeRoute*>(handle)->peak() : 0;
}
AW_API int aw_set_filter_chain(void* handle, const int* kinds,
                                const float* parameters, const int* enabled,
                                int count) noexcept {
    if (!handle) return 0;
    try { return static_cast<NativeRoute*>(handle)->set_filter_chain(
        kinds, parameters, enabled, count) ? 1 : 0; }
    catch (...) { return 0; }
}
AW_API int aw_set_noise_mode(void* handle, int mode) noexcept {
    return handle && static_cast<NativeRoute*>(handle)->set_noise_mode(mode) ? 1 : 0;
}
AW_API int aw_set_effects(void* handle, const float* gains, const int* enabled,
                          int count) noexcept {
    if (!handle) return 0;
    try { return static_cast<NativeRoute*>(handle)->set_effects(
        gains, enabled, count) ? 1 : 0; } catch (...) { return 0; }
}
AW_API int aw_set_output(void* handle, int index, float gain, int muted) noexcept {
    if (!handle) return 0;
    try { return static_cast<NativeRoute*>(handle)->set_output(index, gain, muted) ? 1 : 0; }
    catch (...) { return 0; }
}
AW_API void aw_last_error(void* handle, wchar_t* buffer, std::size_t capacity) noexcept {
    if (!buffer || !capacity) return;
    buffer[0] = L'\0';
    if (!handle) return;
    try {
        const std::wstring message = static_cast<NativeRoute*>(handle)->error();
        wcsncpy_s(buffer, capacity, message.c_str(), _TRUNCATE);
    } catch (...) { buffer[0] = L'\0'; }
}

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "auralweave/vst_plugin.hpp"
#include "vst_editor_window.hpp"
#include "pluginterfaces/gui/iplugview.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "pluginterfaces/vst/ivsthostapplication.h"
#include "pluginterfaces/vst/ivstparameterchanges.h"
#include "pluginterfaces/vst/ivstprocesscontext.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <mutex>
#include <stdexcept>
#include <unordered_map>
#include <vector>

namespace auralweave {
namespace {

using namespace Steinberg;
using namespace Steinberg::Vst;

template<class T>
struct Release {
    void operator()(T* object) const noexcept { if (object) object->release(); }
};
template<class T>
using Ref = std::unique_ptr<T, Release<T>>;

template<class T>
Ref<T> query(FUnknown* object, const TUID& iid) {
    void* result{};
    if (object && object->queryInterface(iid, &result) == kResultOk)
        return Ref<T>(static_cast<T*>(result));
    return {};
}

template<class T>
class HostInterface : public T {
public:
    explicit HostInterface(const TUID& id) noexcept : id_(id) {}
    tresult PLUGIN_API queryInterface(const TUID id, void** object) override {
        if (!object) return kInvalidArgument;
        *object = nullptr;
        if (std::memcmp(id, id_, sizeof(TUID)) && std::memcmp(id, FUnknown_iid, sizeof(TUID)))
            return kNoInterface;
        *object = static_cast<T*>(this);
        addRef();
        return kResultOk;
    }
    uint32 PLUGIN_API addRef() override { return 1; }
    uint32 PLUGIN_API release() override { return 1; }

private:
    const char* id_;
};

class HostApplication final : public HostInterface<IHostApplication> {
public:
    HostApplication() : HostInterface(IHostApplication_iid) {}
    tresult PLUGIN_API getName(String128 name) override {
        constexpr char16 title[] = u"AuralWeave";
        std::copy(std::begin(title), std::end(title), name);
        return kResultOk;
    }
    tresult PLUGIN_API createInstance(TUID, TUID, void** object) override {
        if (object) *object = nullptr;
        return kNoInterface;
    }
};

class ParameterPoint final : public HostInterface<IParamValueQueue> {
public:
    ParameterPoint() : HostInterface(IParamValueQueue_iid) {}
    ParamID id{};
    ParamValue value{};
    ParamID PLUGIN_API getParameterId() override { return id; }
    int32 PLUGIN_API getPointCount() override { return 1; }
    tresult PLUGIN_API getPoint(int32 index, int32& offset, ParamValue& result) override {
        if (index != 0) return kInvalidArgument;
        offset = 0;
        result = value;
        return kResultOk;
    }
    tresult PLUGIN_API addPoint(int32, ParamValue, int32&) override { return kNotImplemented; }
};

class ParameterChanges final : public HostInterface<IParameterChanges> {
public:
    ParameterChanges() : HostInterface(IParameterChanges_iid) {}
    static constexpr int32 capacity = 512;
    std::array<ParameterPoint, capacity> points{};
    int32 count{};
    int32 PLUGIN_API getParameterCount() override { return count; }
    IParamValueQueue* PLUGIN_API getParameterData(int32 index) override {
        return index >= 0 && index < count ? &points[index] : nullptr;
    }
    IParamValueQueue* PLUGIN_API addParameterData(const ParamID&, int32&) override { return nullptr; }
};

class ComponentHandler final : public HostInterface<IComponentHandler> {
public:
    ComponentHandler() : HostInterface(IComponentHandler_iid) {}
    tresult PLUGIN_API beginEdit(ParamID) override { return kResultOk; }
    tresult PLUGIN_API endEdit(ParamID) override { return kResultOk; }
    tresult PLUGIN_API restartComponent(int32) override { return kNotImplemented; }
    tresult PLUGIN_API performEdit(ParamID id, ParamValue value) override {
        if (!std::isfinite(value)) return kInvalidArgument;
        std::lock_guard guard(mutex_);
        auto it = std::find_if(pending_.begin(), pending_.begin() + count_,
                               [id](const auto& point) { return point.id == id; });
        if (it == pending_.begin() + count_) {
            if (count_ == ParameterChanges::capacity) return kResultFalse;
            ++count_;
        }
        *it = {id, value};
        return kResultOk;
    }

    void collect(ParameterChanges& changes) noexcept {
        changes.count = 0;
        std::unique_lock guard(mutex_, std::try_to_lock);
        if (!guard) return;
        for (int32 i = 0; i < count_; ++i) {
            changes.points[i].id = pending_[i].id;
            changes.points[i].value = pending_[i].value;
        }
        changes.count = count_;
        count_ = 0;
    }

private:
    struct Point { ParamID id{}; ParamValue value{}; };
    std::mutex mutex_;
    std::array<Point, ParameterChanges::capacity> pending_{};
    int32 count_{};
};

template<typename Fn>
Fn symbol(HMODULE module, const char* name) noexcept {
#pragma warning(suppress: 4191)
    return reinterpret_cast<Fn>(GetProcAddress(module, name));
}

class Module final {
public:
    using Entry = bool (*)();
    using Factory = IPluginFactory* (PLUGIN_API*)();
    HMODULE library{};
    bool initialized{};

    ~Module() {
        if (initialized)
            if (const auto exit = symbol<Entry>(library, "ExitDll")) exit();
        if (library) FreeLibrary(library);
    }

    static std::shared_ptr<Module> load(const wchar_t* path) {
        // Hosts initialize a module once while any of its instances are alive.
        static std::mutex mutex;
        static std::unordered_map<std::wstring, std::weak_ptr<Module>> modules;
        std::lock_guard guard(mutex);
        auto key = std::filesystem::path(path).lexically_normal().wstring();
        CharLowerBuffW(key.data(), static_cast<DWORD>(key.size()));
        std::erase_if(modules, [](const auto& item) { return item.second.expired(); });
        auto& known = modules[key];
        if (auto module = known.lock()) return module;
        auto module = std::make_shared<Module>();
        module->library = LoadLibraryExW(path, nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module->library)
            throw std::runtime_error("Windows could not load this VST3 module (error " +
                                     std::to_string(GetLastError()) + ")");
        if (const auto init = symbol<Entry>(module->library, "InitDll"); init && !init())
            throw std::runtime_error("VST3 module initialization failed");
        module->initialized = true;
        known = module;
        return module;
    }
};

class Editor final : public HostInterface<IPlugFrame> {
public:
    Editor() : HostInterface(IPlugFrame_iid) {}
    ~Editor() { close(); }
    Editor(const Editor&) = delete;
    Editor& operator=(const Editor&) = delete;

    void show(IEditController& controller, HWND owner, const std::wstring& title) {
        if (window_.window()) {
            window_.show();
            return;
        }

        view_.reset(controller.createView(ViewType::kEditor));
        if (!view_ || view_->isPlatformTypeSupported(kPlatformTypeHWND) != kResultOk) {
            view_.reset();
            throw std::runtime_error("This VST3 effect has no Windows editor");
        }

        try {
            window_.create(owner, L"AuralWeaveVst3Editor", title.c_str(), editor_message, this);
            view_->setFrame(this);
            if (view_->attached(window_.content(), kPlatformTypeHWND) != kResultOk)
                throw std::runtime_error("Could not attach the VST3 editor");

            attached_ = true;
            ViewRect rect{};
            if (view_->getSize(&rect) == kResultOk) resizeView(view_.get(), &rect);
            window_.show();
        } catch (...) {
            close();
            throw;
        }
    }

    tresult PLUGIN_API resizeView(IPlugView* view, ViewRect* rect) override {
        if (!rect || view != view_.get() || !window_.window()) return kInvalidArgument;
        const auto w = rect->getWidth();
        const auto h = rect->getHeight();
        if (w < 1 || w > 8192 || h < 1 || h > 8192) return kInvalidArgument;
        window_.resize(w, h);
        return view_->onSize(rect);
    }

    void close() noexcept {
        if (view_) {
            if (attached_) view_->removed();
            attached_ = false;
            view_->setFrame(nullptr);
            view_.reset();
        }
        window_.destroy();
    }

private:
    static LRESULT editor_message(void* context, HWND window, UINT message, WPARAM param, LPARAM data) noexcept {
        if (message == WM_CLOSE) {
            static_cast<Editor*>(context)->close();
            return 0;
        }
        return DefWindowProcW(window, message, param, data);
    }

    Ref<IPlugView> view_;
    VstEditorWindow window_;
    bool attached_{};
};

class Vst3Plugin final : public VstPlugin {
public:
    ~Vst3Plugin() override { close(); }

    void load(const wchar_t* path, int rate) {
        if (!path || !std::filesystem::path(path).is_absolute())
            throw std::invalid_argument("Enter an absolute path to a VST3 module");
        title_ = std::filesystem::path(path).stem().wstring();
        module_ = Module::load(path);
        const auto factory_fn = symbol<Module::Factory>(module_->library, "GetPluginFactory");
        Ref<IPluginFactory> factory(factory_fn ? factory_fn() : nullptr);
        if (!factory) throw std::runtime_error("Missing VST3 plugin factory");

        auto factory3 = query<IPluginFactory3>(factory.get(), IPluginFactory3_iid);
        if (factory3) factory3->setHostContext(&host_);
        const auto classes = std::clamp(factory->countClasses(), 0, 4096);
        for (int32 i = 0; i < classes && !component_; ++i) {
            PClassInfo info{};
            if (factory->getClassInfo(i, &info) != kResultOk ||
                std::strncmp(info.category, kVstAudioEffectClass, sizeof(info.category)) != 0) continue;
            void* object{};
            if (factory->createInstance(info.cid, IComponent_iid, &object) == kResultOk)
                component_.reset(static_cast<IComponent*>(object));
        }
        if (!component_) throw std::runtime_error("No audio effect in the VST3 module");

        component_->setIoMode(kSimple);
        if (component_->initialize(&host_) != kResultOk)
            throw std::runtime_error("Could not initialize VST3 component");
        component_initialized_ = true;
        processor_ = query<IAudioProcessor>(component_.get(), IAudioProcessor_iid);
        if (!processor_ || processor_->canProcessSampleSize(kSample32) != kResultOk)
            throw std::runtime_error("The VST3 plugin must support 32-bit floating point audio");

        prepare_buses(kInput, inputs_, input_main_);
        prepare_buses(kOutput, outputs_, output_main_);
        for (int pin = 0; pin < 2; ++pin) {
            input_pointers_[pin] = input_pcm_[pin].data();
            output_pointers_[pin] = output_pcm_[pin].data();
        }
        inputs_[input_main_].channelBuffers32 = input_pointers_.data();
        outputs_[output_main_].channelBuffers32 = output_pointers_.data();

        controller_ = query<IEditController>(component_.get(), IEditController_iid);
        if (!controller_) {
            TUID id{};
            void* object{};
            if (component_->getControllerClassId(id) == kResultOk &&
                factory->createInstance(id, IEditController_iid, &object) == kResultOk) {
                controller_.reset(static_cast<IEditController*>(object));
                controller_initialized_ = controller_->initialize(&host_) == kResultOk;
                if (!controller_initialized_) controller_.reset();
            }
        }
        if (controller_) {
            controller_->setComponentHandler(&handler_);
            component_connection_ = query<IConnectionPoint>(component_.get(), IConnectionPoint_iid);
            controller_connection_ = query<IConnectionPoint>(controller_.get(), IConnectionPoint_iid);
            if (component_connection_ && controller_connection_ &&
                component_connection_.get() != controller_connection_.get()) {
                component_connection_->connect(controller_connection_.get());
                controller_connection_->connect(component_connection_.get());
                connected_ = true;
            }
        }
        configure(rate);
    }

    void configure(int rate) override {
        if (rate < 8000 || rate > 384000)
            throw std::invalid_argument("Unsupported VST3 sample rate");
        std::lock_guard guard(control_);
        if (!processor_) throw std::runtime_error("VST3 effect is closed");
        if (active_ && rate == sample_rate_.load()) return;
        deactivate();
        ProcessSetup setup{kRealtime, kSample32, block_size, static_cast<double>(rate)};
        if (processor_->setupProcessing(setup) != kResultOk || component_->setActive(true) != kResultOk)
            throw std::runtime_error("VST3 effect could not start at the input sample rate");
        active_ = true;
        processor_->setProcessing(true);
        sample_rate_.store(rate);
        position_ = 0;
    }

    [[nodiscard]] int sample_rate() const noexcept override { return sample_rate_.load(); }
    [[nodiscard]] bool has_editor() const noexcept override { return controller_ != nullptr; }
    void show_editor(void* owner) override {
        if (!controller_) throw std::runtime_error("This VST3 effect has no editor controller");
        editor_.show(*controller_, static_cast<HWND>(owner), title_);
    }

    void process(float* pcm, std::size_t frames, std::uint16_t channels) noexcept override {
        std::unique_lock guard(control_, std::try_to_lock);
        if (!guard || !active_ || !pcm || (channels != 1 && channels != 2)) return;
        ProcessContext context{};
        context.sampleRate = sample_rate_.load();
        context.tempo = 120.0;
        context.timeSigNumerator = context.timeSigDenominator = 4;
        context.state = ProcessContext::kPlaying | ProcessContext::kTempoValid |
                        ProcessContext::kTimeSigValid | ProcessContext::kProjectTimeMusicValid;
        ProcessData data;
        data.numInputs = static_cast<int32>(inputs_.size());
        data.numOutputs = static_cast<int32>(outputs_.size());
        data.inputs = inputs_.data();
        data.outputs = outputs_.data();
        data.processContext = &context;
        auto& input = inputs_[input_main_];
        auto& output = outputs_[output_main_];

        while (frames) {
            const auto count = static_cast<int32>(std::min<std::size_t>(frames, block_size));
            for (int32 i = 0; i < count; ++i) {
                const auto ix = i * channels;
                const float left = pcm[ix];
                const float right = channels == 2 ? pcm[ix + 1] : left;
                input_pcm_[0][i] = input.numChannels == 1 ? (left + right) * .5F : left;
                input_pcm_[1][i] = right;
            }
            for (auto& channel : output_pcm_) std::fill_n(channel.data(), count, 0.0F);
            input.silenceFlags = output.silenceFlags = 0;
            context.projectTimeSamples = position_;
            context.projectTimeMusic = static_cast<double>(position_) * 2.0 / context.sampleRate;
            data.numSamples = count;
            handler_.collect(changes_);
            data.inputParameterChanges = changes_.count != 0 ? &changes_ : nullptr;

            if (processor_->process(data) != kResultOk) return;
            position_ += count;
            for (int32 i = 0; i < count; ++i) {
                const auto ix = i * channels;
                const auto left = finite_sample(output_pcm_[0][i]);
                const auto right = output.numChannels == 2 ? finite_sample(output_pcm_[1][i]) : left;
                pcm[ix] = channels == 1 ? (left + right) * .5F : left;
                if (channels == 2) pcm[ix + 1] = right;
            }
            pcm += static_cast<std::size_t>(count) * channels;
            frames -= count;
        }
    }

    void close() noexcept override {
        std::lock_guard guard(control_);
        editor_.close();
        deactivate();
        if (controller_) controller_->setComponentHandler(nullptr);
        if (connected_) {
            component_connection_->disconnect(controller_connection_.get());
            controller_connection_->disconnect(component_connection_.get());
            connected_ = false;
        }
        controller_connection_.reset();
        component_connection_.reset();
        if (controller_initialized_) controller_->terminate();
        controller_initialized_ = false;
        controller_.reset();
        processor_.reset();
        if (component_initialized_) component_->terminate();
        component_initialized_ = false;
        component_.reset();
        module_.reset();
    }

private:
    static constexpr int32 block_size = 1024;
    static constexpr int32 max_buses = 32;
    static constexpr int32 max_bus_channels = 64;

    struct BusBuffers {
        std::array<float*, max_bus_channels> pointers{};
    };
    std::array<BusBuffers, max_buses> inactive_inputs_{}, inactive_outputs_{};

    void prepare_buses(BusDirection direction, std::vector<AudioBusBuffers>& buses, std::size_t& main) {
        const auto count = component_->getBusCount(kAudio, direction);
        if (count < 1 || count > max_buses)
            throw std::runtime_error("VST3 effects need an audio input and output");
        auto& inactive = direction == kInput ? inactive_inputs_ : inactive_outputs_;
        buses.resize(count);
        bool found = false;
        for (int32 i = 0; i < count; ++i) {
            BusInfo info{};
            if (component_->getBusInfo(kAudio, direction, i, info) != kResultOk ||
                info.channelCount < 0 || info.channelCount > max_bus_channels)
                throw std::runtime_error("Invalid VST3 bus information");
            auto& bus = buses[i];
            bus.numChannels = info.channelCount;
            bus.channelBuffers32 = inactive[i].pointers.data();
            const bool use = !found && info.busType == kMain;
            if (use) {
                if (info.channelCount != 1 && info.channelCount != 2)
                    throw std::runtime_error("VST3 main buses must be mono or stereo");
                found = true;
                main = static_cast<std::size_t>(i);
            }
            component_->activateBus(kAudio, direction, i, use);
        }
        if (!found) throw std::runtime_error("VST3 effect has no main audio bus");
    }

    void deactivate() noexcept {
        if (active_) {
            processor_->setProcessing(false);
            component_->setActive(false);
            active_ = false;
        }
    }

    static float finite_sample(float value) noexcept { return std::isfinite(value) ? value : 0.0F; }

    std::mutex control_;
    std::shared_ptr<Module> module_;
    HostApplication host_;
    ComponentHandler handler_;
    ParameterChanges changes_;
    Editor editor_;
    Ref<IComponent> component_;
    Ref<IAudioProcessor> processor_;
    Ref<IEditController> controller_;
    Ref<IConnectionPoint> component_connection_, controller_connection_;
    std::wstring title_;
    std::atomic<int> sample_rate_{};
    int64 position_{};
    bool active_{}, component_initialized_{}, controller_initialized_{}, connected_{};
    std::vector<AudioBusBuffers> inputs_, outputs_;
    std::size_t input_main_{}, output_main_{};
    std::array<std::array<float, block_size>, 2> input_pcm_{}, output_pcm_{};
    std::array<float*, 2> input_pointers_{}, output_pointers_{};
};

} // namespace

VstPluginHandle load_vst3(const wchar_t* path, int sample_rate) {
    auto plugin = std::make_shared<Vst3Plugin>();
    plugin->load(path, sample_rate);
    return plugin;
}

} // namespace auralweave

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmdeviceapi.h>
#include <functiondiscoverykeys_devpkey.h>
#include <propidl.h>
#include <wrl/client.h>

#include "auralweave/route_graph.hpp"

#include <stdexcept>

using Microsoft::WRL::ComPtr;

namespace auralweave {

std::vector<AudioDevice> enumerate_endpoints(bool capture) {
    // COM is initialized per caller thread; no global mutable COM state.
    const HRESULT init = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE)
        throw std::runtime_error("CoInitializeEx failed");
    const bool must_uninit = SUCCEEDED(init);

    try {
        ComPtr<IMMDeviceEnumerator> enumerator;
        HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr,
            CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
        if (FAILED(hr)) throw std::runtime_error("MMDeviceEnumerator unavailable");

        const EDataFlow flow = capture ? eCapture : eRender;
        ComPtr<IMMDevice> default_device;
        std::wstring default_id;
        if (SUCCEEDED(enumerator->GetDefaultAudioEndpoint(flow, eMultimedia, &default_device))) {
            LPWSTR id = nullptr;
            if (SUCCEEDED(default_device->GetId(&id)) && id) {
                default_id.assign(id);
                CoTaskMemFree(id);
            }
        }

        ComPtr<IMMDeviceCollection> collection;
        hr = enumerator->EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, &collection);
        if (FAILED(hr)) throw std::runtime_error("EnumAudioEndpoints failed");

        UINT count = 0;
        if (FAILED(collection->GetCount(&count)))
            throw std::runtime_error("GetCount failed");
        std::vector<AudioDevice> result;
        result.reserve(count);
        for (UINT i = 0; i < count; ++i) {
            ComPtr<IMMDevice> device;
            if (FAILED(collection->Item(i, &device))) continue;

            LPWSTR raw_id = nullptr;
            if (FAILED(device->GetId(&raw_id)) || !raw_id) continue;
            std::wstring id(raw_id);
            CoTaskMemFree(raw_id);

            std::wstring name = L"(Unnamed endpoint)";
            ComPtr<IPropertyStore> store;
            if (SUCCEEDED(device->OpenPropertyStore(STGM_READ, &store))) {
                PROPVARIANT value;
                PropVariantInit(&value);
                if (SUCCEEDED(store->GetValue(PKEY_Device_FriendlyName, &value))) {
                    if (value.vt == VT_LPWSTR && value.pwszVal)
                        name.assign(value.pwszVal);
                }
                PropVariantClear(&value);
            }
            const bool is_default = default_id == id;
            result.push_back(AudioDevice{std::move(id), std::move(name), is_default});
        }
        if (must_uninit) CoUninitialize();
        return result;
    } catch (...) {
        if (must_uninit) CoUninitialize();
        throw;
    }
}

} // namespace auralweave

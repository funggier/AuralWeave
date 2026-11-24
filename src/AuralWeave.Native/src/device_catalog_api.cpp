#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>
#include "auralweave/audio_engine_api.h"
#include "auralweave/route_graph.hpp"

#include <cstddef>
#include <cwchar>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace {

void copy_text(wchar_t* destination, std::size_t capacity,
               const std::wstring& value) noexcept {
    if (!destination || capacity == 0) return;
    wcsncpy_s(destination, capacity, value.c_str(), _TRUNCATE);
}

void read_mix(const std::wstring& id, int* rate, int* channels) noexcept {
    if (!rate || !channels) return;
    *rate = 0;
    *channels = 0;
    const HRESULT init = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE) return;
    const bool uninit = SUCCEEDED(init);
    // No mutable COM objects escape this thread.
    {
        ComPtr<IMMDeviceEnumerator> enumerator;
        if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr,
                                       CLSCTX_ALL, IID_PPV_ARGS(&enumerator)))) {
            ComPtr<IMMDevice> device;
            if (SUCCEEDED(enumerator->GetDevice(id.c_str(), &device))) {
                ComPtr<IAudioClient> client;
                if (SUCCEEDED(device->Activate(__uuidof(IAudioClient),
                                               CLSCTX_ALL, nullptr, &client))) {
                    WAVEFORMATEX* mix = nullptr;
                    if (SUCCEEDED(client->GetMixFormat(&mix)) && mix) {
                        *rate = static_cast<int>(mix->nSamplesPerSec);
                        *channels = static_cast<int>(mix->nChannels);
                    }
                    if (mix) CoTaskMemFree(mix);
                }
            }
        }
    }
    if (uninit) CoUninitialize();
}

} // namespace

// Endpoint enumeration C ABI. kind: 0=capture, 1=render.
// A capture device also appears as the synthetic "loopback:<render-id>" in .NET.
// The DLL handles Windows COM enumeration, not a managed NAudio dependency.
AW_API int aw_endpoint_count(int kind) noexcept {
    if (kind != 0 && kind != 1) return -1;
    try {
        const auto endpoints = auralweave::enumerate_endpoints(kind == 0);
        return static_cast<int>(endpoints.size());
    } catch (...) { return -1; }
}

AW_API int aw_endpoint_get(int kind, int index,
                           wchar_t* id, std::size_t id_capacity,
                           wchar_t* name, std::size_t name_capacity,
                           int* is_default, int* sample_rate,
                           int* channels) noexcept {
    if ((kind != 0 && kind != 1) || index < 0 ||
        !id || id_capacity == 0 || !name || name_capacity == 0 ||
        !is_default || !sample_rate || !channels)
        return 0;
    id[0] = L'\0';
    name[0] = L'\0';
    *is_default = 0;
    *sample_rate = 0;
    *channels = 0;
    try {
        const auto endpoints = auralweave::enumerate_endpoints(kind == 0);
        if (static_cast<std::size_t>(index) >= endpoints.size()) return 0;
        const auto& item = endpoints[static_cast<std::size_t>(index)];
        // Windows MMDevice IDs/names should fit these buffers. Do not silently
        // accept truncated endpoint IDs: an invalid ID cannot be opened later.
        if (item.id.size() >= id_capacity || item.name.size() >= name_capacity)
            return 0;
        copy_text(id, id_capacity, item.id);
        copy_text(name, name_capacity, item.name);
        *is_default = item.is_default ? 1 : 0;
        read_mix(item.id, sample_rate, channels);
        return 1;
    } catch (...) { return 0; }
}

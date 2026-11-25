#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <auralweave/virtual_audio_bridge.hpp>
#include <auralweave/virtual_audio_bridge_api.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using auralweave::VirtualAudioBridge;
using auralweave::VirtualBridgeFormat;
using auralweave::VirtualEndpointRole;

static void require(bool ok, const char* message) {
    if (!ok) throw std::runtime_error(message);
    std::cout << "PASS: " << message << '\n';
}
static VirtualEndpointRole role_from(const wchar_t* arg) {
    return std::wstring(arg) == L"in" ? VirtualEndpointRole::input :
           std::wstring(arg) == L"out" ? VirtualEndpointRole::output :
           throw std::runtime_error("Unsupported role");
}
static std::vector<float> signal(std::uint32_t frames, std::uint16_t channels,
                                 float offset = 0.0f) {
    std::vector<float> data(static_cast<std::size_t>(frames) * channels);
    for (std::uint32_t i = 0; i < frames; ++i)
        for (std::uint16_t c = 0; c < channels; ++c)
            data[static_cast<std::size_t>(i) * channels + c] =
                offset + static_cast<float>(i % 997) * 0.0003f + c * 0.00005f;
    return data;
}
static void expect_equal(const std::vector<float>& a,
                         const std::vector<float>& b) {
    if (a != b) throw std::runtime_error("PCM pattern mismatch / cross-channel leak");
}

// Test-exe child. Attaches an exact mapping name over the same Windows
// session (no kernel/device installation), exercising real IPC transport.
static int child_reader(const wchar_t* name, VirtualEndpointRole role, int slot) {
    auto peer = VirtualAudioBridge::attach(name, role, slot);
    const auto fmt = peer.format();
    const auto input = signal(257, fmt.channels, 0.125f);
    std::vector<float> received(input.size());
    std::uint32_t cursor = 0;
    const ULONGLONG deadline = GetTickCount64() + 6000;
    while (cursor < 257 && GetTickCount64() < deadline) {
        const auto count = peer.read(received.data() +
            static_cast<std::size_t>(cursor) * fmt.channels, 257 - cursor);
        cursor += count;
        if (!count) Sleep(1);
    }
    if (cursor != 257) throw std::runtime_error("Child reader timed out");
    expect_equal(input, received);
    return 0;
}

static int child_writer(const wchar_t* name, VirtualEndpointRole role, int slot) {
    auto peer = VirtualAudioBridge::attach(name, role, slot);
    const auto fmt = peer.format();
    auto input = signal(16000, fmt.channels, 0.2f);
    std::uint32_t written = 0;
    const ULONGLONG deadline = GetTickCount64() + 6000;
    while (written < 16000 && GetTickCount64() < deadline) {
        const auto count = peer.write(input.data() +
            static_cast<std::size_t>(written) * fmt.channels,
            16000 - written);
        written += count;
        if (!count) Sleep(1);
    }
    if (written != 16000) throw std::runtime_error("Child writer timed out");
    return 0;
}

static PROCESS_INFORMATION spawn(const wchar_t* executable,
                                 const wchar_t* action,
                                 const std::wstring& name,
                                 VirtualEndpointRole role, std::uint32_t slot) {
    std::wstring command = L"\"";
    command += executable;
    command += L"\" ";
    command += action;
    command += L" \"";
    command += name;
    command += L"\" ";
    command += role == VirtualEndpointRole::input ? L"in " : L"out ";
    command += std::to_wstring(slot);
    STARTUPINFOW info{sizeof(info)};
    PROCESS_INFORMATION child{};
    if (!CreateProcessW(executable, command.data(), nullptr, nullptr,
                        FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &info, &child))
        throw std::runtime_error("Failed to spawn second process");
    return child;
}
static void finish(PROCESS_INFORMATION& child) {
    const auto result = WaitForSingleObject(child.hProcess, 6000);
    DWORD exit_code = 1;
    GetExitCodeProcess(child.hProcess, &exit_code);
    if (result == WAIT_TIMEOUT) TerminateProcess(child.hProcess, 1);
    CloseHandle(child.hThread);
    CloseHandle(child.hProcess);
    require(result == WAIT_OBJECT_0 && exit_code == 0,
            "peer process completes with verified PCM");
}

int wmain(int argc, wchar_t* argv[]) {
    try {
        if (argc == 5 && std::wstring(argv[1]) == L"--reader")
            return child_reader(argv[2], role_from(argv[3]), _wtoi(argv[4]));
        if (argc == 5 && std::wstring(argv[1]) == L"--writer")
            return child_writer(argv[2], role_from(argv[3]), _wtoi(argv[4]));
        if (argc != 1) throw std::runtime_error("Unknown bridge test arguments");

        const VirtualBridgeFormat stereo{48000, 2, 128};
        auto in1 = VirtualAudioBridge::create(VirtualEndpointRole::input, 1, stereo);
        auto in2 = VirtualAudioBridge::create(VirtualEndpointRole::input, 1, stereo);
        auto out1 = VirtualAudioBridge::create(
            VirtualEndpointRole::output, 1, {48000, 1, 128});
        require(in1.name() != in2.name() && in1.name() != out1.name(),
                "separate transport sessions/roles have isolated random mapping names");

        auto peer1 = VirtualAudioBridge::attach(
            in1.name(), VirtualEndpointRole::input, 1);
        auto peer2 = VirtualAudioBridge::attach(
            in2.name(), VirtualEndpointRole::input, 1);
        auto outPeer = VirtualAudioBridge::attach(
            out1.name(), VirtualEndpointRole::output, 1);
        require(in1.owner_process_id() == GetCurrentProcessId() &&
                peer1.owner_process_id() == GetCurrentProcessId() &&
                outPeer.owner_process_id() == GetCurrentProcessId(),
                "native PCM owner PID matches creator process across attached handles");
        require(peer1.format().channels == 2 && outPeer.format().channels == 1,
                "shared PCM format is verified and retained across mappings");

        bool rejected = false;
        try {
            auto bad = VirtualAudioBridge::attach(in1.name(),
                VirtualEndpointRole::output, 1);
        } catch (const std::runtime_error&) { rejected = true; }
        require(rejected, "wrong direction is rejected before PCM access");
        rejected = false;
        try {
            auto bad = VirtualAudioBridge::attach(in1.name(),
                VirtualEndpointRole::input, 2);
        } catch (const std::exception&) { rejected = true; }
        require(rejected, "additional endpoint slots are rejected");
        rejected = false;
        try {
            auto bad = VirtualAudioBridge::create(
                VirtualEndpointRole::output, 2);
        } catch (const std::invalid_argument&) { rejected = true; }
        require(rejected, "unreserved endpoint ID is rejected");
        rejected = false;
        try {
            auto bad = VirtualAudioBridge::attach(in1.name(),
                VirtualEndpointRole::input, 2);
        } catch (const std::exception&) { rejected = true; }
        require(rejected, "attaching an unreserved second endpoint is rejected");
        rejected = false;
        try {
            auto bad = VirtualAudioBridge::create(
                VirtualEndpointRole::output, 1, {48000, 3, 128});
        } catch (const std::invalid_argument&) { rejected = true; }
        require(rejected, "unsupported channel count is rejected");

        const auto first = signal(100, 2);
        require(in1.write(first.data(), 100) == 100,
                "writer publishes interleaved stereo frames");
        std::vector<float> got(95 * 2);
        require(peer1.read(got.data(), 95) == 95, "reader consumes first 95 frames");
        require(std::equal(got.begin(), got.end(), first.begin()),
                "initial PCM content preserved exactly");

        const auto second = signal(100, 2, 0.5f);
        require(in1.write(second.data(), 100) == 100,
                "SPSC producer can wrap a bounded ring");
        std::vector<float> remainder(105 * 2);
        require(peer1.read(remainder.data(), 105) == 105,
                "consumer reads across circular-buffer wrap");
        require(std::equal(remainder.begin(), remainder.begin() + 10,
                           first.begin() + 190) &&
                std::equal(remainder.begin() + 10, remainder.end(),
                           second.begin()),
                "circular frame wrap preserves precise PCM sequence");

        std::array<float, 8> silence{};
        silence.fill(0.75f);
        require(peer1.read(silence.data(), 4, true) == 0 &&
                std::all_of(silence.begin(), silence.end(),
                    [](float x) { return x == 0.0f; }),
                "underrun pads entire audio frame range with silence");
        require(in1.counters().underflow_frames == 4,
                "underrun counter tracks missing frames");

        const auto full = signal(128, 2);
        require(in1.write(full.data(), 128) == 128 &&
                in1.write(full.data(), 11) == 0,
                "bounded ring rejects overrun rather than overwriting unread audio");
        require(in1.counters().overflow_frames == 11 &&
                in1.counters().queued_frames == 128,
                "overrun metrics distinguish dropped input and queued frames");
        std::vector<float> buffer(128 * 2);
        require(peer1.read(buffer.data(), 128) == 128,
                "read capacity correctly recovers from a full buffer");
        expect_equal(full, buffer);

        // Other devices must not see samples from the preceding device.
        std::array<float, 4> other{};
        require(peer2.read(other.data(), 2) == 0 &&
                outPeer.read(other.data(), 2) == 0,
                "two other audio instances remain independent and empty");

        // Actual independent Windows process reads on a virtual-mic bus.
        auto write257 = signal(257, 2, 0.125f);
        PROCESS_INFORMATION reader = spawn(argv[0], L"--reader", in2.name(),
                                           VirtualEndpointRole::input, 1);
        std::uint32_t written = 0;
        const ULONGLONG send_deadline = GetTickCount64() + 5000;
        while (written < 257 && GetTickCount64() < send_deadline) {
            written += in2.write(write257.data() + static_cast<std::size_t>(written) * 2,
                                 257 - written);
            Sleep(1);
        }
        require(written == 257, "parent delivers full PCM payload to child process");
        finish(reader);

        // Child-produced audio flows in the opposite direction through a
        // virtual-output bus, including sustained ring-pressure / wrap.
        auto out2 = VirtualAudioBridge::create(VirtualEndpointRole::output,
                                               1, {48000, 1, 256});
        PROCESS_INFORMATION writer = spawn(argv[0], L"--writer", out2.name(),
                                           VirtualEndpointRole::output, 1);
        std::vector<float> received(16000);
        std::uint32_t read = 0;
        const ULONGLONG recv_deadline = GetTickCount64() + 6000;
        while (read < 16000 && GetTickCount64() < recv_deadline) {
            read += out2.read(received.data() + read, 16000 - read);
            if (read < 16000) Sleep(1);
        }
        require(read == 16000, "16000 frames streamed from second process");
        expect_equal(received, signal(16000, 1, 0.2f));
        finish(writer);

        // Stable exported C ABI is usable by the desktop/native control
        // plane without leaking C++ objects across module boundaries.
        wchar_t c_name[160]{};
        void* owner = aw_bridge_create(1, 1, 48000, 2, 128,
                                       c_name, 160);
        require(owner && wcslen(c_name) > 30,
                "stable C ABI creates user-mode PCM session with mapping name");
        void* subscriber = aw_bridge_attach(c_name, 1, 1);
        require(subscriber != nullptr, "stable C ABI attaches exact role/slot");
        require(aw_bridge_owner_process_id(owner) == GetCurrentProcessId() &&
                aw_bridge_owner_process_id(subscriber) == GetCurrentProcessId() &&
                aw_bridge_owner_process_id(nullptr) == 0,
                "C ABI reports advisory PCM mapping creator PID");
        const auto c_data = signal(12, 2, 0.18f);
        std::vector<float> c_received(c_data.size());
        require(aw_bridge_write(owner, c_data.data(), 12) == 12 &&
                aw_bridge_read(subscriber, c_received.data(), 12, 0) == 12,
                "C ABI transports Float32 interleaved PCM without mutation");
        expect_equal(c_data, c_received);
        AwVirtualBridgeStats stats{};
        require(aw_bridge_get_stats(owner, &stats) == 1 &&
                stats.frames_written == 12 && stats.frames_read == 12 &&
                stats.sample_rate == 48000 && stats.channels == 2,
                "C ABI returns bounded buffer statistics and exact format");
        aw_bridge_close(owner);
        require(aw_bridge_get_stats(subscriber, &stats) == 1 &&
                stats.closed_by_owner == 1,
                "C ABI observes owner shutdown without dangling handles");
        aw_bridge_close(subscriber);

        void* bad_role = aw_bridge_attach(c_name, 2, 1);
        require(bad_role == nullptr,
                "C ABI rejects attachment with incorrect role");
        wchar_t message[220]{};
        require(aw_bridge_last_error(message, 220) > 1,
                "C ABI returns useful error for failed attachment");

        in1.close();
        require(peer1.counters().producer_closed,
                "clean owner shutdown is visible to an already-attached peer");
        rejected = false;
        try {
            auto bad = VirtualAudioBridge::attach(
                peer1.name(), VirtualEndpointRole::input, 1);
        } catch (const std::runtime_error&) { rejected = true; }
        require(rejected, "closed PCM sessions reject new peers");

        std::cout << "ALL VIRTUAL AUDIO BRIDGE IPC TESTS PASSED\n";
        return 0;
    } catch (const std::exception& ex) {
        std::cerr << "VIRTUAL AUDIO BRIDGE TEST FAILED: " << ex.what() << '\n';
        return 1;
    }
}

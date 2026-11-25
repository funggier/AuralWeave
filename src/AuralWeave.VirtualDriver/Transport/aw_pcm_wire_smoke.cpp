// Shared Windows 10 user-mode test for the *same* source compiled by WDK.
// Does NOT load or install a kernel-mode driver.
#include "aw_pcm_wire.h"
#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <vector>

static void check(bool good, const char* label) {
    if (!good) throw std::runtime_error(label);
    std::cout << "PASS: " << label << '\n';
}
static AW_PCM_WIRE_PACKET packet(uint32_t opcode, uint32_t role,
                                  uint32_t format = AW_PCM_WIRE_FORMAT_F32,
                                  uint32_t channels = 2, uint32_t frames = 0) {
    AW_PCM_WIRE_PACKET p{};
    p.magic = AW_PCM_WIRE_MAGIC;
    p.version = AW_PCM_WIRE_VERSION;
    p.total_bytes = sizeof(p);
    p.opcode = opcode;
    p.role = role;
    p.slot = AW_PCM_WIRE_SLOT;
    p.sample_format = format;
    p.sample_rate = 48000;
    p.channels = channels;
    p.bits_per_sample = format == AW_PCM_WIRE_FORMAT_F32 ? 32 : 16;
    p.frame_count = frames;
    p.request_id = 77;
    if (opcode == AW_PCM_WIRE_INPUT_WRITE) {
        p.payload_bytes = frames * channels * (p.bits_per_sample / 8);
        p.total_bytes += p.payload_bytes;
    }
    return p;
}

int main() {
    try {
        check(sizeof(AW_PCM_WIRE_PACKET) == 64 &&
              offsetof(AW_PCM_WIRE_PACKET, request_id) == 56,
              "native/kernel protocol is fixed 64-byte POD with aligned request ID");

        auto helloIn = packet(AW_PCM_WIRE_HELLO, AW_PCM_WIRE_INPUT);
        auto helloOut = packet(AW_PCM_WIRE_HELLO, AW_PCM_WIRE_OUTPUT);
        check(aw_pcm_wire_validate(&helloIn, sizeof(helloIn)) == AW_PCM_WIRE_OK &&
              aw_pcm_wire_validate(&helloOut, sizeof(helloOut)) == AW_PCM_WIRE_OK,
              "hello validates for sole input and output role");
        auto input = packet(AW_PCM_WIRE_INPUT_WRITE, AW_PCM_WIRE_INPUT,
                            AW_PCM_WIRE_FORMAT_S16, 1, 256);
        check(aw_pcm_wire_validate(&input, input.total_bytes) == AW_PCM_WIRE_OK,
              "PCM16 mono input transfer validates");
        input = packet(AW_PCM_WIRE_INPUT_WRITE, AW_PCM_WIRE_INPUT,
                       AW_PCM_WIRE_FORMAT_F32, 2, 256);
        check(aw_pcm_wire_validate(&input, input.total_bytes) == AW_PCM_WIRE_OK,
              "Float32 stereo input transfer validates");
        auto output = packet(AW_PCM_WIRE_OUTPUT_READ, AW_PCM_WIRE_OUTPUT,
                             AW_PCM_WIRE_FORMAT_F32, 2, 256);
        check(aw_pcm_wire_validate(&output, sizeof(output)) == AW_PCM_WIRE_OK,
              "output read request is header-only; no user pointer");
        auto status = packet(AW_PCM_WIRE_GET_STATUS, AW_PCM_WIRE_INPUT);
        check(aw_pcm_wire_validate(&status, sizeof(status)) == AW_PCM_WIRE_OK,
              "status request validates");

        auto malformed = helloIn;
        malformed.magic = 0;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_MAGIC,
              "wrong magic rejected");
        malformed = helloIn;
        ++malformed.version;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_VERSION,
              "future protocol version rejected");
        malformed = helloIn;
        malformed.slot = 2;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_DIRECTION,
              "second virtual device slot rejected");
        malformed = helloIn;
        malformed.role = 99;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_DIRECTION,
              "unrecognized driver role rejected");
        malformed = helloIn;
        malformed.flags = 1;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_FLAGS,
              "unknown flags rejected rather than interpreted");
        malformed = helloIn;
        malformed.reserved = 1;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_FLAGS,
              "nonzero reserved field rejected");
        malformed = helloIn;
        malformed.sample_rate = 0;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_FORMAT,
              "invalid sample rate rejected");
        malformed = helloIn;
        malformed.channels = 32;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_FORMAT,
              "overwide channel layout rejected");
        malformed = helloIn;
        malformed.bits_per_sample = 16;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_FORMAT,
              "inconsistent PCM format/bit depth rejected");
        malformed = helloIn;
        malformed.opcode = 666;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_OPERATION,
              "unrecognized IOCTL opcode rejected");
        malformed = helloIn;
        malformed.total_bytes = 0xffffffffu;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_LENGTH,
              "integer-overflow / forged total byte size rejected");
        malformed = input;
        ++malformed.payload_bytes;
        ++malformed.total_bytes;
        check(aw_pcm_wire_validate(&malformed, malformed.total_bytes) == AW_PCM_WIRE_BAD_LENGTH,
              "input data not an integral number of PCM frames rejected");
        malformed = input;
        malformed.role = AW_PCM_WIRE_OUTPUT;
        check(aw_pcm_wire_validate(&malformed, malformed.total_bytes) == AW_PCM_WIRE_BAD_DIRECTION,
              "write cannot cross to virtual speaker channel");
        malformed = output;
        malformed.role = AW_PCM_WIRE_INPUT;
        check(aw_pcm_wire_validate(&malformed, malformed.total_bytes) == AW_PCM_WIRE_BAD_DIRECTION,
              "read cannot cross to virtual microphone channel");
        malformed = output;
        malformed.frame_count = AW_PCM_WIRE_MAX_FRAMES + 1;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_LENGTH,
              "excessive frame count rejected");
        malformed = helloIn;
        malformed.frame_count = 1;
        check(aw_pcm_wire_validate(&malformed, sizeof(malformed)) == AW_PCM_WIRE_BAD_OPERATION,
              "HELLO cannot transfer audio frames");
        check(aw_pcm_wire_validate(nullptr, 0) == AW_PCM_WIRE_NULL,
              "null packet rejected");
        malformed = helloIn;
        check(aw_pcm_wire_validate(&malformed, 8) == AW_PCM_WIRE_BAD_LENGTH,
              "truncated header is rejected before accessing fields");

        std::array<uint8_t, 512> storage{};
        AW_PCM_BYTE_RING ring{};
        check(aw_pcm_ring_init(&ring, storage.data(), static_cast<uint32_t>(storage.size()), 8) == 1 &&
              ring.capacity_frames == 64,
              "caller-owned ring allocates no memory and tracks complete stereo frames");

        std::vector<uint8_t> first(50 * 8);
        for (size_t i = 0; i < first.size(); ++i) first[i] = static_cast<uint8_t>(i % 251);
        check(aw_pcm_ring_write(&ring, first.data(), 50) == 50,
              "ring writes exact interleaved PCM without conversion");
        std::vector<uint8_t> read(40 * 8);
        check(aw_pcm_ring_read(&ring, read.data(), 40, 0) == 40 &&
              std::memcmp(read.data(), first.data(), read.size()) == 0,
              "ring returns 40 frames in order");
        std::vector<uint8_t> second(50 * 8);
        for (size_t i = 0; i < second.size(); ++i) second[i] = static_cast<uint8_t>(0x90 + i % 89);
        check(aw_pcm_ring_write(&ring, second.data(), 50) == 50,
              "ring wraps inside a fixed nonpaged-size buffer");
        std::vector<uint8_t> all(60 * 8);
        check(aw_pcm_ring_read(&ring, all.data(), 60, 0) == 60 &&
              std::memcmp(all.data(), first.data() + 40 * 8, 10 * 8) == 0 &&
              std::memcmp(all.data() + 10 * 8, second.data(), second.size()) == 0,
              "ring crossing both boundaries preserves entire PCM frame order");

        check(aw_pcm_ring_write(&ring, first.data(), 50) == 50 &&
              aw_pcm_ring_write(&ring, second.data(), 50) == 14 &&
              ring.overflow == 36,
              "bounded frame buffer rejects overflow and counts dropped frames");
        std::vector<uint8_t> full(64 * 8);
        check(aw_pcm_ring_read(&ring, full.data(), 64, 0) == 64 &&
              aw_pcm_ring_queued(&ring) == 0,
              "consumer drains a full ring");
        std::array<uint8_t, 8 * 4> silence{};
        silence.fill(0xcc);
        check(aw_pcm_ring_read(&ring, silence.data(), 4, 1) == 0 &&
              ring.underflow == 4,
              "underflow emits silence and reports missing frames");
        bool all_zero = true;
        for (auto b : silence) all_zero = all_zero && b == 0;
        check(all_zero, "every byte of a silent PCM frame is zero");

        std::array<uint8_t, 512> other{};
        AW_PCM_BYTE_RING secondRing{};
        check(aw_pcm_ring_init(&secondRing, other.data(), static_cast<uint32_t>(other.size()), 8) &&
              aw_pcm_ring_queued(&secondRing) == 0 &&
              ring.written != secondRing.written,
              "input and output rings use independently owned state");
        check(!aw_pcm_ring_init(&secondRing, other.data(), 13, 8) &&
              !aw_pcm_ring_init(&secondRing, other.data(), static_cast<uint32_t>(other.size()), 3),
              "invalid capacity / sample stride rejected");
        aw_pcm_ring_reset(&ring);
        check(ring.written == 0 && ring.read == 0 &&
              ring.overflow == 0 && ring.underflow == 0,
              "transport reset clears cursors and counters");

        // Sustained sequence with bounded producer/consumer pressure.
        std::array<uint8_t, 512> source{}, sink{};
        for (uint32_t iteration = 0; iteration < 1000; ++iteration) {
            for (size_t i = 0; i < source.size(); ++i)
                source[i] = static_cast<uint8_t>((iteration + i) % 251);
            if (aw_pcm_ring_write(&ring, source.data(), 64) != 64 ||
                aw_pcm_ring_read(&ring, sink.data(), 64, 0) != 64 ||
                source != sink)
                throw std::runtime_error("Sustained PCM sequence differs across wraps");
        }
        check(ring.written == 64000 && ring.read == 64000,
              "64,000 PCM frames survived 1,000 full-capacity wrap cycles");
        std::cout << "ALL AURALWEAVE PCM WIRE / RING TESTS PASSED\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "FAIL: PCM transport test: " << e.what() << "\n";
        return 1;
    }
}

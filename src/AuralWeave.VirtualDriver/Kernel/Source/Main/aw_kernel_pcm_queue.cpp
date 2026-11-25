// Original AuralWeave kernel transport staging (MIT).
// WaveRT and the separate control device share these internal queues.
#include "definitions.h"
#include "aw_kernel_pcm_queue.h"

namespace {
constexpr aw_pcm_u32 kStorageBytes = 65536u;

// Nonpaged image .data/.bss storage. This avoids allocations in real-time
// callbacks and ensures that all PCM touched under a spin lock is resident.
struct KernelEndpoint {
    KSPIN_LOCK lock;
    AW_PCM_BYTE_RING ring;
    aw_pcm_u8 storage[kStorageBytes];
    aw_pcm_u32 rate;
    aw_pcm_u32 channels;
    aw_pcm_u32 bits;
    aw_pcm_u32 attached;
    bool blocked; // DRM rejection and producer writes synchronized by lock
};
KernelEndpoint g_input{};
KernelEndpoint g_output{};

KernelEndpoint* get_endpoint(aw_pcm_u32 role) noexcept {
    if (role == AW_PCM_WIRE_INPUT) return &g_input;
    if (role == AW_PCM_WIRE_OUTPUT) return &g_output;
    return nullptr;
}
bool valid_format(aw_pcm_u32 rate, aw_pcm_u32 channels,
                  aw_pcm_u32 bits) noexcept {
    return rate >= 8000 && rate <= 384000 &&
        (channels == 1 || channels == 2) &&
        (bits == 16 || bits == 32);
}
} // namespace

bool AwKernelPcmDescribe(aw_pcm_u32 role,
                         AwKernelPcmSnapshot* result) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint || !result) return false;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    result->sampleRate = endpoint->rate;
    result->channels = endpoint->channels;
    result->bitsPerSample = endpoint->bits;
    result->queuedFrames = aw_pcm_ring_queued(&endpoint->ring);
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    return true;
}

bool AwKernelPcmTransfer(const AW_PCM_WIRE_PACKET& request, aw_pcm_u8* pcm,
                         aw_pcm_u32& transferred) noexcept {
    auto* endpoint = get_endpoint(request.role);
    if (!endpoint || !pcm) return false;

    // Keep format, protection and copy atomic with WaveRT attach/detach.
    transferred = 0;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    bool compatible = true;
    if (endpoint->attached > 0 && !endpoint->blocked) {
        compatible = request.sample_rate == endpoint->rate &&
            request.channels == endpoint->channels &&
            request.bits_per_sample == endpoint->bits;
        if (compatible) {
            if (request.role == AW_PCM_WIRE_INPUT)
                transferred = aw_pcm_ring_write(&endpoint->ring, pcm, request.frame_count);
            else
                transferred = aw_pcm_ring_read(&endpoint->ring, pcm, request.frame_count, 0);
        }
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    return compatible;
}

void AwKernelPcmInitialize() noexcept {
    // Called before PortCls initializes adapters/streams; no publication of
    // these endpoints exists before DriverEntry calls this function.
    KeInitializeSpinLock(&g_input.lock);
    KeInitializeSpinLock(&g_output.lock);
    g_input.attached = 0;
    g_output.attached = 0;
    g_input.blocked = false;
    g_output.blocked = false;
    g_input.rate = g_output.rate = 0;
    g_input.channels = g_output.channels = 0;
    g_input.bits = g_output.bits = 0;
    RtlZeroMemory(&g_input.ring, sizeof(g_input.ring));
    RtlZeroMemory(&g_output.ring, sizeof(g_output.ring));
}

bool AwKernelPcmAttach(aw_pcm_u32 role, aw_pcm_u32 sampleRate,
                       aw_pcm_u32 channels, aw_pcm_u32 bitsPerSample) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint || !valid_format(sampleRate, channels, bitsPerSample))
        return false;
    const auto frameBytes = channels * bitsPerSample / 8u;
    // aw_pcm_ring_init requires an integral number of sample frames.
    // 65536 is evenly divisible by all supported frame strides (2,4,8).
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    bool allowed = false;
    if (endpoint->attached == 0) {
        allowed = aw_pcm_ring_init(&endpoint->ring, endpoint->storage,
                                   kStorageBytes, frameBytes) == 1;
        if (allowed) {
            endpoint->rate = sampleRate;
            endpoint->channels = channels;
            endpoint->bits = bitsPerSample;
            endpoint->attached = 1;
            // Only a fresh role session may clear the conservative
            // content-protection latch. Existing streams cannot.
            endpoint->blocked = false;
        }
    } else if (endpoint->attached < 0xFFFFFFFFu &&
               endpoint->rate == sampleRate &&
               endpoint->channels == channels &&
               endpoint->bits == bitsPerSample) {
        ++endpoint->attached;
        allowed = true;
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    return allowed;
}

void AwKernelPcmDetach(aw_pcm_u32 role) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    if (endpoint->attached > 0 && --endpoint->attached == 0) {
        aw_pcm_ring_reset(&endpoint->ring);
        // Preserve the deny state until the next successful first attach.
        // A concurrent newly-started stream cannot clear it prematurely.
        endpoint->rate = 0;
        endpoint->channels = 0;
        endpoint->bits = 0;
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
}

void AwKernelPcmFlush(aw_pcm_u32 role) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    if (endpoint->attached > 0)
        aw_pcm_ring_reset(&endpoint->ring);
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
}

void AwKernelPcmDisableOutput() noexcept {
    auto* endpoint = &g_output;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    // Acquiring the same lock as the producer ensures a write which
    // began before DRM changes is flushed, and all later writes fail.
    endpoint->blocked = true;
    if (endpoint->attached > 0)
        aw_pcm_ring_reset(&endpoint->ring);
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
}

aw_pcm_u32 AwKernelPcmWrite(aw_pcm_u32 role, const aw_pcm_u8* bytes,
                            aw_pcm_u32 frames, aw_pcm_u32 bytesPerFrame) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint || !bytes || !frames || !bytesPerFrame) return 0;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    const auto accepted =
        endpoint->attached > 0 && !endpoint->blocked &&
        endpoint->ring.bytes_per_frame == bytesPerFrame
        ? aw_pcm_ring_write(&endpoint->ring, bytes, frames) : 0u;
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    return accepted;
}

aw_pcm_u32 AwKernelPcmRead(aw_pcm_u32 role, aw_pcm_u8* bytes,
                           aw_pcm_u32 frames, aw_pcm_u32 bytesPerFrame,
                           bool silenceOnUnderrun) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint || !bytes || !frames || !bytesPerFrame) return 0;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    const auto received =
        endpoint->attached > 0 && endpoint->ring.bytes_per_frame == bytesPerFrame
        ? aw_pcm_ring_read(&endpoint->ring, bytes, frames,
                           silenceOnUnderrun ? 1 : 0) : 0u;
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    if (silenceOnUnderrun && received == 0) {
        // Also silence the format-mismatch or stopped-stream case, not only
        // the ring underrun case. This is safe with resident DMA memory.
        RtlZeroMemory(bytes, static_cast<SIZE_T>(frames) * bytesPerFrame);
    }
    return received;
}

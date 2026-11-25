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
    aw_pcm_u32 running;
    aw_pcm_u64 generation;
    aw_pcm_u64 waveRtFrames;
    bool blocked; // DRM rejection and producer writes synchronized by lock
};
KernelEndpoint g_input{};
KernelEndpoint g_output{};

KernelEndpoint* get_endpoint(aw_pcm_u32 role) noexcept {
    if (role == AW_PCM_WIRE_INPUT) return &g_input;
    if (role == AW_PCM_WIRE_OUTPUT) return &g_output;
    return nullptr;
}
void reset_generation(KernelEndpoint& endpoint) noexcept {
    aw_pcm_ring_reset(&endpoint.ring);
    if (++endpoint.generation == 0)
        ++endpoint.generation;
    endpoint.waveRtFrames = 0;
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
    const bool running = endpoint->running != 0;
    result->sampleRate = running ? endpoint->rate : 0u;
    result->channels = running ? endpoint->channels : 0u;
    result->bitsPerSample = running ? endpoint->bits : 0u;
    result->queuedFrames = aw_pcm_ring_queued(&endpoint->ring);
    result->generation = endpoint->generation;
    result->waveRtFrames = endpoint->waveRtFrames;
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    return true;
}

bool AwKernelPcmTransfer(AW_PCM_WIRE_PACKET& request, aw_pcm_u8* pcm) noexcept {
    auto* endpoint = get_endpoint(request.role);
    if (!endpoint || !pcm) return false;

    // Match the generation and format in the same critical section as the copy.
    aw_pcm_u32 transferred = 0;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    bool compatible = true;
    if (endpoint->running != 0 && !endpoint->blocked &&
        request.generation == endpoint->generation) {
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
    request.frame_count = transferred;
    request.generation = endpoint->generation;
    request.wave_rt_frames = endpoint->waveRtFrames;
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    return compatible;
}

void AwKernelPcmInitialize() noexcept {
    // Called before PortCls initializes adapters/streams; no publication of
    // these endpoints exists before DriverEntry calls this function.
    KeInitializeSpinLock(&g_input.lock);
    KeInitializeSpinLock(&g_output.lock);
    g_input.attached = g_output.attached = 0;
    g_input.running = g_output.running = 0;
    g_input.generation = g_output.generation = 1;
    g_input.waveRtFrames = g_output.waveRtFrames = 0;
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
        if (endpoint->running != 0) {
            endpoint->running = 0;
            reset_generation(*endpoint);
        }
        // Preserve the deny state until the next successful first attach.
        // A concurrent newly-started stream cannot clear it prematurely.
        endpoint->rate = 0;
        endpoint->channels = 0;
        endpoint->bits = 0;
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
}

void AwKernelPcmSetRunning(aw_pcm_u32 role, bool running) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    if (running && endpoint->running < endpoint->attached) {
        if (endpoint->running++ == 0)
            reset_generation(*endpoint);
    } else if (!running && endpoint->running > 0) {
        if (--endpoint->running == 0)
            reset_generation(*endpoint);
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
}

void AwKernelPcmDisableOutput() noexcept {
    auto* endpoint = &g_output;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    // Acquiring the same lock as the producer ensures a write which
    // began before DRM changes is flushed, and all later writes fail.
    if (!endpoint->blocked) {
        endpoint->blocked = true;
        reset_generation(*endpoint);
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
}

aw_pcm_u32 AwKernelPcmWrite(aw_pcm_u32 role, const aw_pcm_u8* bytes,
                            aw_pcm_u32 frames, aw_pcm_u32 bytesPerFrame) noexcept {
    auto* endpoint = get_endpoint(role);
    if (!endpoint || !bytes || !frames || !bytesPerFrame) return 0;
    KIRQL oldIrql;
    KeAcquireSpinLock(&endpoint->lock, &oldIrql);
    aw_pcm_u32 accepted = 0;
    if (endpoint->running != 0 && !endpoint->blocked &&
        endpoint->ring.bytes_per_frame == bytesPerFrame) {
        endpoint->waveRtFrames += frames;
        accepted = aw_pcm_ring_write(&endpoint->ring, bytes, frames);
    }
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
    aw_pcm_u32 received = 0;
    const bool active = endpoint->running != 0 &&
        endpoint->ring.bytes_per_frame == bytesPerFrame;
    if (active) {
        endpoint->waveRtFrames += frames;
        received = aw_pcm_ring_read(&endpoint->ring, bytes, frames,
                                    silenceOnUnderrun ? 1 : 0);
    }
    KeReleaseSpinLock(&endpoint->lock, oldIrql);
    if (silenceOnUnderrun && !active) {
        // Also silence the format-mismatch or stopped-stream case, not only
        // the ring underrun case. This is safe with resident DMA memory.
        RtlZeroMemory(bytes, static_cast<SIZE_T>(frames) * bytesPerFrame);
    }
    return received;
}

// Standalone portable protocol + caller-synchronized PCM FIFO.
// Original AuralWeave source (MIT). No CRT allocation or OS dependencies.
#include "aw_pcm_wire.h"

namespace {
bool supported_format(const AW_PCM_WIRE_PACKET& packet) {
    if (packet.sample_rate < 8000u || packet.sample_rate > 384000u ||
        (packet.channels != 1u && packet.channels != 2u))
        return false;
    return (packet.sample_format == AW_PCM_WIRE_FORMAT_S16 &&
            packet.bits_per_sample == 16u) ||
           (packet.sample_format == AW_PCM_WIRE_FORMAT_F32 &&
            packet.bits_per_sample == 32u);
}

void copy_bytes(aw_pcm_u8* dst, const aw_pcm_u8* src, aw_pcm_size_t count) {
    for (aw_pcm_size_t i = 0; i < count; ++i) dst[i] = src[i];
}
void zero_bytes(aw_pcm_u8* dst, aw_pcm_size_t count) {
    for (aw_pcm_size_t i = 0; i < count; ++i) dst[i] = 0;
}
}

extern "C" AW_PCM_WIRE_STATUS aw_pcm_wire_validate(
    const AW_PCM_WIRE_PACKET* packet, aw_pcm_size_t received_bytes) {
    if (!packet) return AW_PCM_WIRE_NULL;
    if (received_bytes < sizeof(AW_PCM_WIRE_PACKET))
        return AW_PCM_WIRE_BAD_LENGTH;
    if (packet->magic != AW_PCM_WIRE_MAGIC)
        return AW_PCM_WIRE_BAD_MAGIC;
    if (packet->version != AW_PCM_WIRE_VERSION)
        return AW_PCM_WIRE_BAD_VERSION;
    if (packet->total_bytes != received_bytes ||
        received_bytes > sizeof(AW_PCM_WIRE_PACKET) + AW_PCM_WIRE_MAX_PAYLOAD ||
        packet->payload_bytes > AW_PCM_WIRE_MAX_PAYLOAD ||
        static_cast<aw_pcm_u64>(packet->payload_bytes) +
            sizeof(AW_PCM_WIRE_PACKET) != received_bytes)
        return AW_PCM_WIRE_BAD_LENGTH;
    if ((packet->role != AW_PCM_WIRE_INPUT && packet->role != AW_PCM_WIRE_OUTPUT) ||
        packet->slot != AW_PCM_WIRE_SLOT)
        return AW_PCM_WIRE_BAD_DIRECTION;
    if (packet->flags != 0 || packet->reserved != 0)
        return AW_PCM_WIRE_BAD_FLAGS;
    if (!supported_format(*packet))
        return AW_PCM_WIRE_BAD_FORMAT;
    if (packet->frame_count > AW_PCM_WIRE_MAX_FRAMES)
        return AW_PCM_WIRE_BAD_LENGTH;

    switch (packet->opcode) {
    case AW_PCM_WIRE_HELLO:
    case AW_PCM_WIRE_GET_STATUS:
        return (packet->frame_count == 0 && packet->payload_bytes == 0)
            ? AW_PCM_WIRE_OK : AW_PCM_WIRE_BAD_OPERATION;
    case AW_PCM_WIRE_INPUT_WRITE: {
        if (packet->role != AW_PCM_WIRE_INPUT ||
            packet->frame_count == 0)
            return AW_PCM_WIRE_BAD_DIRECTION;
        const aw_pcm_u64 bytes_per_frame =
            static_cast<aw_pcm_u64>(packet->channels) *
            (packet->bits_per_sample / 8u);
        return static_cast<aw_pcm_u64>(packet->frame_count) * bytes_per_frame ==
                   packet->payload_bytes
            ? AW_PCM_WIRE_OK : AW_PCM_WIRE_BAD_LENGTH;
    }
    case AW_PCM_WIRE_OUTPUT_READ:
        if (packet->role != AW_PCM_WIRE_OUTPUT ||
            packet->frame_count == 0)
            return AW_PCM_WIRE_BAD_DIRECTION;
        return packet->payload_bytes == 0
            ? AW_PCM_WIRE_OK : AW_PCM_WIRE_BAD_OPERATION;
    default:
        return AW_PCM_WIRE_BAD_OPERATION;
    }
}

extern "C" int aw_pcm_ring_init(AW_PCM_BYTE_RING* ring, aw_pcm_u8* buffer,
                                 aw_pcm_u32 capacity_bytes, aw_pcm_u32 bytes_per_frame) {
    if (!ring || !buffer ||
        (bytes_per_frame != 2 && bytes_per_frame != 4 && bytes_per_frame != 8) ||
        capacity_bytes < 64u * bytes_per_frame ||
        capacity_bytes > 262144u ||
        capacity_bytes % bytes_per_frame != 0)
        return 0;
    ring->data = buffer;
    ring->capacity_frames = capacity_bytes / bytes_per_frame;
    ring->bytes_per_frame = bytes_per_frame;
    ring->written = 0;
    ring->read = 0;
    ring->overflow = 0;
    ring->underflow = 0;
    return 1;
}

extern "C" void aw_pcm_ring_reset(AW_PCM_BYTE_RING* ring) {
    if (!ring) return;
    ring->read = 0;
    ring->written = 0;
    ring->overflow = 0;
    ring->underflow = 0;
}

extern "C" aw_pcm_u32 aw_pcm_ring_queued(const AW_PCM_BYTE_RING* ring) {
    if (!ring || !ring->data || !ring->capacity_frames ||
        ring->read > ring->written ||
        ring->written - ring->read > ring->capacity_frames)
        return 0;
    return static_cast<aw_pcm_u32>(ring->written - ring->read);
}

extern "C" aw_pcm_u32 aw_pcm_ring_write(AW_PCM_BYTE_RING* ring,
                                      const aw_pcm_u8* bytes, aw_pcm_u32 frames) {
    if (!ring || !ring->data || !ring->capacity_frames ||
        !ring->bytes_per_frame || (frames && !bytes) ||
        ring->read > ring->written ||
        ring->written - ring->read > ring->capacity_frames)
        return 0;
    const aw_pcm_u32 free = ring->capacity_frames - aw_pcm_ring_queued(ring);
    const aw_pcm_u32 count = frames < free ? frames : free;
    const aw_pcm_u32 offset = static_cast<aw_pcm_u32>(ring->written % ring->capacity_frames);
    const aw_pcm_u32 first = count < ring->capacity_frames - offset
        ? count : ring->capacity_frames - offset;
    copy_bytes(ring->data + static_cast<aw_pcm_size_t>(offset) * ring->bytes_per_frame,
               bytes, static_cast<aw_pcm_size_t>(first) * ring->bytes_per_frame);
    if (count > first)
        copy_bytes(ring->data,
                   bytes + static_cast<aw_pcm_size_t>(first) * ring->bytes_per_frame,
                   static_cast<aw_pcm_size_t>(count - first) * ring->bytes_per_frame);
    ring->written += count;
    ring->overflow += frames - count;
    return count;
}

extern "C" aw_pcm_u32 aw_pcm_ring_read(AW_PCM_BYTE_RING* ring,
                                     aw_pcm_u8* bytes, aw_pcm_u32 frames,
                                     int fill_silence) {
    if (!ring || !ring->data || !ring->capacity_frames ||
        !ring->bytes_per_frame || (frames && !bytes) ||
        ring->read > ring->written ||
        ring->written - ring->read > ring->capacity_frames)
        return 0;
    const aw_pcm_u32 queued = aw_pcm_ring_queued(ring);
    const aw_pcm_u32 count = frames < queued ? frames : queued;
    const aw_pcm_u32 offset = static_cast<aw_pcm_u32>(ring->read % ring->capacity_frames);
    const aw_pcm_u32 first = count < ring->capacity_frames - offset
        ? count : ring->capacity_frames - offset;
    copy_bytes(bytes,
               ring->data + static_cast<aw_pcm_size_t>(offset) * ring->bytes_per_frame,
               static_cast<aw_pcm_size_t>(first) * ring->bytes_per_frame);
    if (count > first)
        copy_bytes(bytes + static_cast<aw_pcm_size_t>(first) * ring->bytes_per_frame,
                   ring->data,
                   static_cast<aw_pcm_size_t>(count - first) * ring->bytes_per_frame);
    ring->read += count;
    ring->underflow += frames - count;
    if (fill_silence && count < frames)
        zero_bytes(bytes + static_cast<aw_pcm_size_t>(count) * ring->bytes_per_frame,
                   static_cast<aw_pcm_size_t>(frames - count) * ring->bytes_per_frame);
    return count;
}

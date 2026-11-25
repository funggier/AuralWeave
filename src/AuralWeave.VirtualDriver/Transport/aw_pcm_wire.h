#pragma once
/*
 * AuralWeave virtual audio prototype: fixed-width IOCTL data contract.
 * Original AuralWeave MIT source. This header is independent of Windows
 * headers and usable by both WDK kernel driver and Windows 10 user-mode.
 *
 * A packet is a SINGLE IOCTL transfer, not a shared-memory pointer. The
 * kernel must copy and validate bytes from a METHOD_BUFFERED system buffer.
 * No user pointer, HANDLE, kernel address, or variable-length string is
 * allowed on this wire. This does NOT implement IOCTL dispatch or security.
 */
/* Avoid user-mode vcruntime.h in kernel compilation.
 * Both Windows 10 and Windows 11 x64 use 32-bit unsigned int and
 * 64-bit unsigned long long. These exact widths are asserted below. */
typedef unsigned char aw_pcm_u8;
typedef unsigned int aw_pcm_u32;
typedef unsigned long long aw_pcm_u64;
typedef unsigned long long aw_pcm_size_t;

#define AW_PCM_WIRE_MAGIC 0x31575041u /* AuralWeave PCM magic, little-endian */
#define AW_PCM_WIRE_VERSION 2u
#define AW_PCM_WIRE_MAX_FRAMES 2048u
#define AW_PCM_WIRE_MAX_PAYLOAD 16384u
#define AW_PCM_WIRE_SLOT 1u
#define AW_PCM_WIRE_INPUT 1u   /* Studio -> virtual microphone/capture */
#define AW_PCM_WIRE_OUTPUT 2u  /* Virtual speaker/render -> Studio */
#define AW_PCM_WIRE_FORMAT_S16 1u
#define AW_PCM_WIRE_FORMAT_F32 2u
#define AW_PCM_WIRE_FORMAT_S32 3u
/* CTL_CODE(FILE_DEVICE_UNKNOWN=0x22, 0x800, METHOD_BUFFERED, READ|WRITE). */
#define AW_PCM_IOCTL_EXCHANGE 0x0022E000u
#define AW_PCM_WIRE_HELLO 1u
#define AW_PCM_WIRE_INPUT_WRITE 2u
#define AW_PCM_WIRE_OUTPUT_READ 3u
#define AW_PCM_WIRE_GET_STATUS 4u

typedef struct AW_PCM_WIRE_PACKET {
    aw_pcm_u32 magic;
    aw_pcm_u32 version;
    aw_pcm_u32 total_bytes;
    aw_pcm_u32 opcode;
    aw_pcm_u32 role;
    aw_pcm_u32 slot;
    aw_pcm_u32 sample_format;
    aw_pcm_u32 sample_rate;
    aw_pcm_u32 channels;
    aw_pcm_u32 bits_per_sample;
    aw_pcm_u32 frame_count;
    aw_pcm_u32 payload_bytes;
    aw_pcm_u32 flags;
    aw_pcm_u32 reserved;
    aw_pcm_u64 request_id;
    aw_pcm_u64 generation;       /* expected generation on data commands */
    aw_pcm_u64 wave_rt_frames;   /* reply-only frame position in this generation */
} AW_PCM_WIRE_PACKET;

#if defined(__cplusplus)
static_assert(sizeof(AW_PCM_WIRE_PACKET) == 80, "Transport ABI must not change");
static_assert(sizeof(aw_pcm_u32) == 4 && sizeof(aw_pcm_u64) == 8,
              "Transport requires Windows x64 fixed-width types");
#endif

typedef enum AW_PCM_WIRE_STATUS {
    AW_PCM_WIRE_OK = 0,
    AW_PCM_WIRE_NULL = 1,
    AW_PCM_WIRE_BAD_MAGIC = 2,
    AW_PCM_WIRE_BAD_VERSION = 3,
    AW_PCM_WIRE_BAD_LENGTH = 4,
    AW_PCM_WIRE_BAD_DIRECTION = 5,
    AW_PCM_WIRE_BAD_FORMAT = 6,
    AW_PCM_WIRE_BAD_OPERATION = 7,
    AW_PCM_WIRE_BAD_FLAGS = 8,
    AW_PCM_WIRE_BAD_GENERATION = 9
} AW_PCM_WIRE_STATUS;

#ifdef __cplusplus
extern "C" {
#endif

/* Parse with memcpy before calling: caller must have already validated that
 * at least sizeof(AW_PCM_WIRE_PACKET) bytes are accessible. Do not read fields from an unchecked IRP. */
AW_PCM_WIRE_STATUS aw_pcm_wire_validate(
    const AW_PCM_WIRE_PACKET* packet, aw_pcm_size_t received_bytes);

typedef struct AW_PCM_BYTE_RING {
    aw_pcm_u8* data; /* caller-owned nonpaged in kernel, never user pointer */
    aw_pcm_u32 capacity_frames;
    aw_pcm_u32 bytes_per_frame;
    aw_pcm_u64 written;
    aw_pcm_u64 read;
    aw_pcm_u64 overflow;
    aw_pcm_u64 underflow;
} AW_PCM_BYTE_RING;

/* CALLER SYNCHRONIZES producer/consumer (e.g. KSPIN_LOCK in kernel).
 * No allocation, no system calls, and no waiting inside ring operations. */
int aw_pcm_ring_init(AW_PCM_BYTE_RING* ring, aw_pcm_u8* buffer,
                     aw_pcm_u32 capacity_bytes, aw_pcm_u32 bytes_per_frame);
void aw_pcm_ring_reset(AW_PCM_BYTE_RING* ring);
aw_pcm_u32 aw_pcm_ring_write(AW_PCM_BYTE_RING* ring,
                           const aw_pcm_u8* bytes, aw_pcm_u32 frames);
aw_pcm_u32 aw_pcm_ring_read(AW_PCM_BYTE_RING* ring, aw_pcm_u8* bytes,
                          aw_pcm_u32 frames, int fill_silence);
aw_pcm_u32 aw_pcm_ring_queued(const AW_PCM_BYTE_RING* ring);

#ifdef __cplusplus
}
#endif

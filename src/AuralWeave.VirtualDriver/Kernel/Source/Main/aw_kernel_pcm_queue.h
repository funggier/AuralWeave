#pragma once
// Original AuralWeave MIT code, separate from the MS-PL sample modules.
// Kernel-internal, bounded WaveRT PCM queues. They are NOT exposed through
// an IOCTL, a device interface, user memory mapping, or driver installation.
//
// IRQL contract: Initialize at PASSIVE_LEVEL from DriverEntry; the other
// methods are callable through DISPATCH_LEVEL; any byte buffers supplied by
// WaveRT must be resident/nonpaged and accessible for the full request.
#include "../../../Transport/aw_pcm_wire.h"

void AwKernelPcmInitialize() noexcept;

// Attach a WaveRT stream with a consistent frame layout. Other streams may
// share the same role/format; incompatible formats do not use the bridge.
bool AwKernelPcmAttach(aw_pcm_u32 role, aw_pcm_u32 sampleRate,
                       aw_pcm_u32 channels, aw_pcm_u32 bitsPerSample) noexcept;
void AwKernelPcmDetach(aw_pcm_u32 role) noexcept;
// Erase all queued PCM when DRM restrictions change or stream resets.
void AwKernelPcmFlush(aw_pcm_u32 role) noexcept;
// Fail-closed policy: after protected content is observed, block ALL writes
// to this role while any stream remains open. The entire output queue is
// reset within the same KSPIN_LOCK critical section as the write guard.
void AwKernelPcmDisableOutput() noexcept;

// Studio -> virtual microphone: future secure producer calls Write(INPUT).
// Virtual speaker -> Studio: WaveRT render path calls Write(OUTPUT).
aw_pcm_u32 AwKernelPcmWrite(aw_pcm_u32 role, const aw_pcm_u8* bytes,
                            aw_pcm_u32 frames, aw_pcm_u32 bytesPerFrame) noexcept;

// Virtual microphone: WaveRT capture path calls Read(INPUT) and pads silence.
// Virtual speaker: future secure consumer calls Read(OUTPUT).
aw_pcm_u32 AwKernelPcmRead(aw_pcm_u32 role, aw_pcm_u8* bytes,
                           aw_pcm_u32 frames, aw_pcm_u32 bytesPerFrame,
                           bool silenceOnUnderrun) noexcept;

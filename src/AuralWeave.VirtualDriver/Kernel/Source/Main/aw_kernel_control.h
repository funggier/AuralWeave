#pragma once
// MIT original AuralWeave control-plane code. PortCls remains responsible
// for the actual audio device stacks and unrelated IRPs.
//
// Prototype security boundary:
//   - only SYSTEM and Administrators can open this control device
//   - no device pointers or raw kernel memory are exported
//   - one bounded METHOD_BUFFERED request at a time
//   - only role=Input PCM32 writes and role=Output PCM16 reads
//   - NOT a signed or deployed device driver
#include "../../../Transport/aw_pcm_wire.h"

extern "C" {
NTSTATUS AwKernelControlCreate(_In_ PDRIVER_OBJECT driver) noexcept;
void AwKernelControlDestroy() noexcept;
}

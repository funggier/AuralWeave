// Original MIT AuralWeave kernel control plane — UNSIGNED PROTOTYPE.
// PortCls owns the audio PnP device stacks. We intercept only the exact
// separately created control device object and forward all other IRPs to
// PcDispatchIrp. Do NOT install this driver without kernel-runtime QA.
#include "definitions.h"
#include "aw_kernel_control.h"
#include "aw_kernel_pcm_queue.h"
#include <wdmsec.h>

#pragma comment(lib, "wdmsec.lib")

namespace {
PDEVICE_OBJECT g_controlDevice = nullptr;
UNICODE_STRING g_controlSymbolicLink;
// AuralWeave-private device setup class GUID, not GUID_DEVCLASS_MEDIA.
// Distinct class is important to prevent security policy confusion.
const GUID kControlClass = {
    0x0796959d, 0xacc0, 0x4d0d,
    {0x9a, 0x9c, 0x65, 0x8e, 0x52, 0xec, 0xee, 0x29}
};
constexpr ULONG kMaxExchange =
    sizeof(AW_PCM_WIRE_PACKET) + AW_PCM_WIRE_MAX_PAYLOAD;

NTSTATUS Complete(_In_ PIRP irp, NTSTATUS result,
                  ULONG_PTR information = 0) noexcept {
    irp->IoStatus.Status = result;
    irp->IoStatus.Information = NT_SUCCESS(result) ? information : 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return result;
}

// Restrict to exact device name, not an arbitrary child namespace path.
NTSTATUS ControlOpenClose(PDEVICE_OBJECT device, PIRP irp) {
    if (device != g_controlDevice)
        return PcDispatchIrp(device, irp);
    auto* stack = IoGetCurrentIrpStackLocation(irp);
    if (stack->MajorFunction == IRP_MJ_CREATE &&
        stack->FileObject != nullptr &&
        stack->FileObject->FileName.Length != 0)
        return Complete(irp, STATUS_OBJECT_NAME_NOT_FOUND);
    return Complete(irp, STATUS_SUCCESS);
}

NTSTATUS ControlCleanup(PDEVICE_OBJECT device, PIRP irp) {
    if (device != g_controlDevice)
        return PcDispatchIrp(device, irp);
    return Complete(irp, STATUS_SUCCESS);
}

NTSTATUS Exchange(PIRP irp) noexcept {
    auto* stack = IoGetCurrentIrpStackLocation(irp);
    auto& fields = stack->Parameters.DeviceIoControl;
    // Only the single private buffered IOCTL is accepted. The I/O manager
    // enforces FILE_READ_ACCESS | FILE_WRITE_ACCESS before dispatch.
    if (fields.IoControlCode != AW_PCM_IOCTL_EXCHANGE)
        return Complete(irp, STATUS_INVALID_DEVICE_REQUEST);
    if (fields.InputBufferLength < sizeof(AW_PCM_WIRE_PACKET) ||
        fields.InputBufferLength > kMaxExchange ||
        fields.OutputBufferLength < sizeof(AW_PCM_WIRE_PACKET) ||
        fields.OutputBufferLength > kMaxExchange ||
        irp->AssociatedIrp.SystemBuffer == nullptr)
        return Complete(irp, STATUS_INVALID_BUFFER_SIZE);

    auto* data = static_cast<aw_pcm_u8*>(irp->AssociatedIrp.SystemBuffer);
    AW_PCM_WIRE_PACKET req{};
    RtlCopyMemory(&req, data, sizeof(req));
    if (aw_pcm_wire_validate(&req, fields.InputBufferLength) != AW_PCM_WIRE_OK)
        return Complete(irp, STATUS_INVALID_PARAMETER);

    // The existing microphone's advertised 32-bit format is PCM S32, NOT
    // IEEE float32. The speaker is 16-bit PCM. Never interpret mismatched
    // numeric representations as each other.
    if ((req.role == AW_PCM_WIRE_INPUT &&
         (req.sample_format != AW_PCM_WIRE_FORMAT_S32 ||
          req.bits_per_sample != 32u)) ||
        (req.role == AW_PCM_WIRE_OUTPUT &&
         (req.sample_format != AW_PCM_WIRE_FORMAT_S16 ||
          req.bits_per_sample != 16u)))
        return Complete(irp, STATUS_NOT_SUPPORTED);

    const bool read = req.opcode == AW_PCM_WIRE_OUTPUT_READ;
    if (read || req.opcode == AW_PCM_WIRE_INPUT_WRITE) {
        const aw_pcm_u32 frameBytes = req.channels * req.bits_per_sample / 8u;
        if (read && fields.OutputBufferLength <
            sizeof(AW_PCM_WIRE_PACKET) + req.frame_count * frameBytes)
            return Complete(irp, STATUS_BUFFER_TOO_SMALL);

        // No attached stream, or protected playback, is a normal no-progress
        // result for this role. The other role can keep exchanging frames.
        aw_pcm_u32 copied = 0;
        if (!AwKernelPcmTransfer(req, data + sizeof(AW_PCM_WIRE_PACKET), copied))
            return Complete(irp, STATUS_NOT_SUPPORTED);

        req.frame_count = copied;
        req.payload_bytes = read ? copied * frameBytes : 0;
        req.total_bytes = sizeof(AW_PCM_WIRE_PACKET) + req.payload_bytes;
        RtlCopyMemory(data, &req, sizeof(req));
        return Complete(irp, STATUS_SUCCESS, req.total_bytes);
    }

    AwKernelPcmSnapshot snapshot{};
    if (!AwKernelPcmDescribe(req.role, &snapshot))
        return Complete(irp, STATUS_INVALID_PARAMETER);

    if (req.opcode == AW_PCM_WIRE_GET_STATUS) {
        // A result-only metadata header. frame_count is the queued count.
        // Protected playback leaves the mirror queue empty.
        req.frame_count = snapshot.queuedFrames;
    } else if (req.opcode != AW_PCM_WIRE_HELLO) {
        return Complete(irp, STATUS_INVALID_DEVICE_REQUEST);
    }

    req.payload_bytes = 0;
    req.total_bytes = sizeof(AW_PCM_WIRE_PACKET);
    // For status we return the actual negotiated frame geometry;
    // zero when no compatible WaveRT streams are attached.
    req.sample_rate = snapshot.sampleRate;
    req.channels = snapshot.channels;
    // With no attached stream, preserve the caller's valid PCM
    // representation. The rate/channels=0 indicate no active endpoint;
    // a client can still parse an empty status response.
    if (snapshot.bitsPerSample != 0) {
        req.bits_per_sample = snapshot.bitsPerSample;
        req.sample_format = snapshot.bitsPerSample == 16u
            ? AW_PCM_WIRE_FORMAT_S16 : AW_PCM_WIRE_FORMAT_S32;
    }
    RtlCopyMemory(data, &req, sizeof(req));
    return Complete(irp, STATUS_SUCCESS, sizeof(req));
}

NTSTATUS ControlDeviceControl(PDEVICE_OBJECT device, PIRP irp) {
    if (device != g_controlDevice)
        return PcDispatchIrp(device, irp);
    return Exchange(irp);
}
} // namespace

extern "C" NTSTATUS AwKernelControlCreate(
    _In_ PDRIVER_OBJECT driver) noexcept {
    if (g_controlDevice) return STATUS_INVALID_DEVICE_STATE;
    UNICODE_STRING name;
    UNICODE_STRING sddl;
    RtlInitUnicodeString(&name, L"\\Device\\AuralWeaveVirtualAudioControl");
    RtlInitUnicodeString(&g_controlSymbolicLink,
                         L"\\DosDevices\\AuralWeaveVirtualAudioControl");
    RtlInitUnicodeString(&sddl, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");

    // Redirect ONLY our control-object IRPs. PnP, Power, and Kernel
    // Streaming IRPs continue via PortCls's normal handlers.
    auto* oldCreate = driver->MajorFunction[IRP_MJ_CREATE];
    auto* oldClose = driver->MajorFunction[IRP_MJ_CLOSE];
    auto* oldCleanup = driver->MajorFunction[IRP_MJ_CLEANUP];
    auto* oldDeviceControl = driver->MajorFunction[IRP_MJ_DEVICE_CONTROL];
    driver->MajorFunction[IRP_MJ_CREATE] = ControlOpenClose;
    driver->MajorFunction[IRP_MJ_CLOSE] = ControlOpenClose;
    driver->MajorFunction[IRP_MJ_CLEANUP] = ControlCleanup;
    driver->MajorFunction[IRP_MJ_DEVICE_CONTROL] = ControlDeviceControl;

    NTSTATUS result = IoCreateDeviceSecure(
        driver, 0, &name, FILE_DEVICE_UNKNOWN,
        FILE_DEVICE_SECURE_OPEN, FALSE,
        &sddl, &kControlClass, &g_controlDevice);
    if (NT_SUCCESS(result)) {
        result = IoCreateSymbolicLink(&g_controlSymbolicLink, &name);
        if (NT_SUCCESS(result)) {
            // g_controlDevice exists and dispatch hooks are already active.
            g_controlDevice->Flags &= ~DO_DEVICE_INITIALIZING;
            return STATUS_SUCCESS;
        }
        IoDeleteDevice(g_controlDevice);
        g_controlDevice = nullptr;
    }
    // On failure restore handlers so PortCls remains fully responsible
    // for all IRPs; leave no dangling control device or DOS symlink.
    driver->MajorFunction[IRP_MJ_CREATE] = oldCreate;
    driver->MajorFunction[IRP_MJ_CLOSE] = oldClose;
    driver->MajorFunction[IRP_MJ_CLEANUP] = oldCleanup;
    driver->MajorFunction[IRP_MJ_DEVICE_CONTROL] = oldDeviceControl;
    return result;
}

extern "C" void AwKernelControlDestroy() noexcept {
    if (!g_controlDevice) return;
    IoDeleteSymbolicLink(&g_controlSymbolicLink);
    IoDeleteDevice(g_controlDevice);
    g_controlDevice = nullptr;
}

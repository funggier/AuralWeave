using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Exact v2 METHOD_BUFFERED control-packet codec. Independent of whether
/// an installable/signed kernel driver exists. Device access is admin-only.
/// WAVEFORMAT: virtual mic PCM_S32 stereo @48k, virtual speaker PCM_S16
/// stereo @48k. No pointer/handle is sent through the IOCTL.
/// </summary>
public static class VirtualKernelPcmProtocol
{
    public const uint Magic = 0x31575041;
    public const uint Version = 2;
    public const uint Ioctl = 0x0022E000;
    public const int HeaderSize = 80;
    public const int MaxFrames = 2048;
    public const int MaxPayload = 16384;
    public const uint InputRole = 1;
    public const uint OutputRole = 2;
    public const uint Signed16Format = 1;
    public const uint Signed32Format = 3;
    public const uint Hello = 1;
    public const uint InputWrite = 2;
    public const uint OutputRead = 3;
    public const uint Status = 4;

    public readonly record struct Header(
        uint Opcode, uint Role, uint Format, uint SampleRate,
        uint Channels, uint Bits, uint Frames, uint PayloadBytes,
        ulong RequestId, ulong Generation = 0, ulong WaveRtFrames = 0);

    public static byte[] Encode(Header header, ReadOnlySpan<byte> payload)
    {
        if (header.Role is not (InputRole or OutputRole) ||
            header.Format is not (Signed16Format or Signed32Format) ||
            header.SampleRate is < 8000 or > 384000 ||
            header.Channels is not (1u or 2u) ||
            header.Bits != (header.Format == Signed16Format ? 16u : 32u) ||
            payload.Length != header.PayloadBytes || payload.Length > MaxPayload ||
            header.Frames > MaxFrames)
            throw new ArgumentException("Invalid bounded PCM header or payload.");
        var bytes = new byte[HeaderSize + payload.Length];
        var span = bytes.AsSpan();
        static void U32(Span<byte> buffer, int offset, uint v) =>
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset), v);
        U32(span, 0, Magic);
        U32(span, 4, Version);
        U32(span, 8, (uint)bytes.Length);
        U32(span, 12, header.Opcode);
        U32(span, 16, header.Role);
        U32(span, 20, 1); // singleton Windows hardware device slot
        U32(span, 24, header.Format);
        U32(span, 28, header.SampleRate);
        U32(span, 32, header.Channels);
        U32(span, 36, header.Bits);
        U32(span, 40, header.Frames);
        U32(span, 44, header.PayloadBytes);
        U32(span, 48, 0);
        U32(span, 52, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(span[56..], header.RequestId);
        BinaryPrimitives.WriteUInt64LittleEndian(span[64..], header.Generation);
        BinaryPrimitives.WriteUInt64LittleEndian(span[72..], header.WaveRtFrames);
        payload.CopyTo(span[HeaderSize..]);
        return bytes;
    }

    public static Header Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
            throw new InvalidDataException("Kernel PCM reply header is truncated.");
        static uint U32(ReadOnlySpan<byte> b, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(b[offset..]);
        if (U32(buffer, 0) != Magic || U32(buffer, 4) != Version ||
            U32(buffer, 8) != buffer.Length ||
            U32(buffer, 20) != 1 ||
            U32(buffer, 48) != 0 || U32(buffer, 52) != 0 ||
            U32(buffer, 44) != buffer.Length - HeaderSize)
            throw new InvalidDataException("Kernel PCM reply violated version/size/flags.");
        // Treat even a privileged kernel response as untrusted wire data.
        // Status is the only opcode whose frame count represents the full
        // queue rather than a <=2048-frame transfer.
        var opcode = U32(buffer, 12);
        var role = U32(buffer, 16);
        var format = U32(buffer, 24);
        var rate = U32(buffer, 28);
        var channels = U32(buffer, 32);
        var bits = U32(buffer, 36);
        var frames = U32(buffer, 40);
        var payloadBytes = U32(buffer, 44);
        var isMetadata = opcode is Hello or Status;
        var waveRtFrames = BinaryPrimitives.ReadUInt64LittleEndian(buffer[72..]);
        if (role is not (InputRole or OutputRole) ||
            opcode is not (Hello or InputWrite or OutputRead or Status) ||
            format != (role == InputRole ? Signed32Format : Signed16Format) ||
            bits != (format == Signed16Format ? 16u : 32u) ||
            (rate != 0 && (rate < 8000 || rate > 384000)) ||
            channels > 2 || (rate == 0) != (channels == 0) ||
            (!isMetadata && (rate == 0 || frames > MaxFrames)) ||
            (opcode == Hello && frames != 0) ||
            (isMetadata && payloadBytes != 0) ||
            (rate == 0 && (frames != 0 || waveRtFrames != 0)) ||
            payloadBytes > MaxPayload)
            throw new InvalidDataException("Kernel PCM reply contains invalid format, role, operation or bounds.");
        return new Header(opcode, role, format, rate, channels, bits,
            frames, payloadBytes, BinaryPrimitives.ReadUInt64LittleEndian(buffer[56..]),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer[64..]), waveRtFrames);
    }
}

/// <summary>System IOCTL transport. Consumers can test against a mock endpoint
/// before an independently signed kernel driver is installed.</summary>
public interface IVirtualKernelPcmExchange
{
    byte[] Exchange(byte[] request, int maxResponse);
}

public sealed class WindowsVirtualKernelPcmExchange : IVirtualKernelPcmExchange, IDisposable
{
    public const string ControlDevice = @"\\.\AuralWeaveVirtualAudioControl";
    private readonly SafeFileHandle _handle;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;

    public WindowsVirtualKernelPcmExchange()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This driver transport requires Windows.");
        _handle = CreateFileW(ControlDevice, GenericRead | GenericWrite,
            FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error,
                "AuralWeave signed kernel control device is unavailable or access denied. " +
                "Do not install the unsigned WDK prototype.");
        }
    }

    public byte[] Exchange(byte[] request, int maxResponse)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Length is < VirtualKernelPcmProtocol.HeaderSize or
            > VirtualKernelPcmProtocol.HeaderSize + VirtualKernelPcmProtocol.MaxPayload ||
            maxResponse is < VirtualKernelPcmProtocol.HeaderSize or
            > VirtualKernelPcmProtocol.HeaderSize + VirtualKernelPcmProtocol.MaxPayload)
            throw new ArgumentOutOfRangeException(nameof(maxResponse));
        var destination = new byte[maxResponse];
        if (!DeviceIoControl(_handle, VirtualKernelPcmProtocol.Ioctl,
            request, (uint)request.Length,
            destination, (uint)destination.Length, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "AuralWeave secure kernel PCM exchange failed.");
        if (returned < VirtualKernelPcmProtocol.HeaderSize || returned > destination.Length)
            throw new InvalidDataException("Invalid kernel PCM reply length.");
        return destination.AsSpan(0, (int)returned).ToArray();
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, FileShare shareMode,
        IntPtr securityAttributes, FileMode creationDisposition,
        uint flags, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device,
        uint ioctl, byte[] inBuffer, uint inputSize,
        [Out] byte[] outBuffer, uint outputSize, out uint bytesReturned,
        IntPtr overlapped);
}

/// <summary>
/// Data path for a future trusted elevated broker. Multiple Portable Studio
/// clients share this broker, not independent handles to the admin-only
/// kernel control device. An elevated broker is NOT currently shipped.
/// </summary>
public sealed class VirtualKernelPcmClient(IVirtualKernelPcmExchange exchange)
{
    private long _sequence;

    private (VirtualKernelPcmProtocol.Header Header, byte[] Bytes) Call(
        uint role, uint opcode, uint format, uint bits, uint frames,
        ReadOnlySpan<byte> payload, int maxResponse, ulong generation = 0)
    {
        var requestId = (ulong)Interlocked.Increment(ref _sequence);
        var request = VirtualKernelPcmProtocol.Encode(
            new VirtualKernelPcmProtocol.Header(opcode, role, format,
                48000, 2, bits, frames, (uint)payload.Length, requestId, generation), payload);
        var reply = exchange.Exchange(request, maxResponse);
        var result = VirtualKernelPcmProtocol.Decode(reply);
        if (result.RequestId != requestId || result.Role != role ||
            result.Opcode != opcode || result.Format != format ||
            result.Bits != bits || result.Generation == 0 ||
            (generation != 0 && result.Generation != generation &&
             (result.Frames != 0 || result.PayloadBytes != 0)) ||
            (opcode is VirtualKernelPcmProtocol.InputWrite or VirtualKernelPcmProtocol.OutputRead &&
             (result.SampleRate != 48000 || result.Channels != 2)) ||
            (opcode != VirtualKernelPcmProtocol.Status &&
             result.Frames > VirtualKernelPcmProtocol.MaxFrames))
            throw new InvalidDataException("Kernel reply does not match request format or correlation ID.");
        return (result, reply);
    }

    public VirtualKernelPcmProtocol.Header Query(uint role)
    {
        var format = role == VirtualKernelPcmProtocol.InputRole
            ? VirtualKernelPcmProtocol.Signed32Format : VirtualKernelPcmProtocol.Signed16Format;
        return Call(role, VirtualKernelPcmProtocol.Status, format,
            format == VirtualKernelPcmProtocol.Signed32Format ? 32u : 16u,
            0, ReadOnlySpan<byte>.Empty, VirtualKernelPcmProtocol.HeaderSize).Header;
    }

    /// <summary>Send [-1,+1] Float32 stereo audio to PCM32 microphone.</summary>
    public uint WriteMicrophone(ReadOnlySpan<float> pcmStereo, ulong generation)
    {
        if (pcmStereo.Length == 0 || pcmStereo.Length % 2 != 0 ||
            pcmStereo.Length > VirtualKernelPcmProtocol.MaxFrames * 2)
            throw new ArgumentException("Expected 1..2048 stereo frames.", nameof(pcmStereo));
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        var payload = new byte[pcmStereo.Length * sizeof(int)];
        for (var i = 0; i < pcmStereo.Length; ++i)
        {
            var v = float.IsFinite(pcmStereo[i]) ? Math.Clamp(pcmStereo[i], -1f, 1f) : 0f;
            var value = v <= -1f ? int.MinValue
                : v >= 1f ? int.MaxValue
                : (int)Math.Round((double)v * int.MaxValue);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(i * 4), value);
        }
        var frames = (uint)pcmStereo.Length / 2u;
        var decoded = Call(VirtualKernelPcmProtocol.InputRole,
            VirtualKernelPcmProtocol.InputWrite,
            VirtualKernelPcmProtocol.Signed32Format, 32u,
            frames, payload, VirtualKernelPcmProtocol.HeaderSize, generation).Header;
        if (decoded.PayloadBytes != 0 || decoded.Frames > frames)
            throw new InvalidDataException("Kernel microphone ACK has invalid size.");
        return decoded.Frames;
    }

    /// <summary>Read stereo PCM16 output as Float32 without stealing other
    /// clients' user-mode data. Only the single trusted broker should read.</summary>
    public float[] ReadSpeaker(int frames, ulong generation)
    {
        if (frames is < 1 or > VirtualKernelPcmProtocol.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(frames));
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        var (header, reply) = Call(VirtualKernelPcmProtocol.OutputRole,
            VirtualKernelPcmProtocol.OutputRead,
            VirtualKernelPcmProtocol.Signed16Format, 16u,
            (uint)frames, ReadOnlySpan<byte>.Empty,
            VirtualKernelPcmProtocol.HeaderSize + frames * 4, generation);
        if (header.Frames > frames || header.PayloadBytes != header.Frames * 4u)
            throw new InvalidDataException("Kernel speaker reply frame alignment failed.");
        var result = new float[header.Frames * 2u];
        for (var i = 0; i < result.Length; ++i)
        {
            var v = BinaryPrimitives.ReadInt16LittleEndian(reply.AsSpan(
                VirtualKernelPcmProtocol.HeaderSize + i * 2));
            result[i] = v / 32768f;
        }
        return result;
    }
}

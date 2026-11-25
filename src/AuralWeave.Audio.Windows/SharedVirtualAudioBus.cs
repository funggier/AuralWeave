using System.Collections.Generic;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// User-mode multi-client PCM coordinator for one logical Windows virtual role.
/// Each client owns its own SPSC mapping. A coordinator reads all contributor
/// channels and broadcasts the mixed result into independent listener channels.
///
/// IMPORTANT: This is an explicit, developer-only hub, NOT an installable
/// Windows audio endpoint or a self-discovering Windows service.
/// </summary>
public sealed class SharedVirtualAudioBus : IDisposable
{
    private readonly Dictionary<string, NativeVirtualAudioBridge> _contributors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeVirtualAudioBridge> _listeners =
        new(StringComparer.Ordinal);
    private readonly float[] _mix;
    private readonly float[] _scratch;
    private readonly List<string> _disconnected = new(16);
    private int _pumpCycles;
    private bool _disposed;

    public SharedVirtualAudioBus(uint role, uint sampleRate = 48000,
        uint channels = 2, int blockFrames = 256)
    {
        if (role is not (1u or 2u))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (sampleRate is < 8000 or > 384000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is not (1u or 2u))
            throw new ArgumentOutOfRangeException(nameof(channels));
        if (blockFrames is < 32 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(blockFrames));
        Role = role;
        SampleRate = sampleRate;
        Channels = channels;
        BlockFrames = blockFrames;
        _mix = new float[blockFrames * (int)channels];
        _scratch = new float[_mix.Length];
    }

    public uint Role { get; }
    public uint SampleRate { get; }
    public uint Channels { get; }
    public int BlockFrames { get; }
    public int Contributors => _contributors.Count;
    public int Listeners => _listeners.Count;
    public ulong FramesMixed { get; private set; }
    public ulong FramesDelivered { get; private set; }
    public ulong FramesDropped { get; private set; }
    public ulong DisconnectedContributors { get; private set; }
    public ulong DisconnectedListeners { get; private set; }

    /// <summary>
    /// Register a client-owned SPSC bridge to receive its PCM contribution.
    /// The client writes to its own bridge; this bus reads exactly one copy.
    /// </summary>
    public void AddContributor(string mappingName, uint? expectedOwnerProcessId = null) =>
        Add(mappingName, _contributors, _listeners, expectedOwnerProcessId);

    /// <summary>
    /// Register a client-owned SPSC bridge to deliver independent PCM.
    /// One slow listener never consumes another listener's audio.
    /// </summary>
    public void AddListener(string mappingName, uint? expectedOwnerProcessId = null) =>
        Add(mappingName, _listeners, _contributors, expectedOwnerProcessId);

    private void Add(string name, Dictionary<string, NativeVirtualAudioBridge> target,
                     Dictionary<string, NativeVirtualAudioBridge> other,
                     uint? expectedOwnerProcessId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (target.ContainsKey(name) || other.ContainsKey(name))
            throw new InvalidOperationException("PCM mapping is already registered.");
        if (target.Count >= 16)
            throw new InvalidOperationException("Too many user-mode PCM clients (maximum 16).");
        var bridge = NativeVirtualAudioBridge.Attach(name, Role, 1);
        var stats = bridge.GetStats();
        if ((expectedOwnerProcessId.HasValue &&
             (expectedOwnerProcessId.Value == 0 ||
              bridge.OwnerProcessId != expectedOwnerProcessId.Value)) ||
            stats.SampleRate != SampleRate || stats.Channels != Channels ||
            stats.CapacityFrames < (uint)BlockFrames)
        {
            bridge.Dispose();
            throw new InvalidOperationException(
                "Virtual PCM owner process or format/capacity does not match the shared bus.");
        }
        target.Add(name, bridge);
    }

    public bool RemoveContributor(string name) => Remove(_contributors, name);
    public bool RemoveListener(string name) => Remove(_listeners, name);

    private bool Remove(Dictionary<string, NativeVirtualAudioBridge> table, string name)
    {
        ThrowIfDisposed();
        if (!table.Remove(name, out var bridge))
            return false;
        bridge.Dispose();
        return true;
    }

    /// <summary>
    /// Remove peer mappings after their owning Portable process disappears.
    /// In addition to the normal closed flag, the native bridge checks the
    /// per-owner HANDLE's lifetime, including after a hard process kill.
    /// This is advisory user-mode recovery, not client authorization.
    /// Call only on the coordinator's pump thread or while it is stopped.
    /// </summary>
    public int PruneDisconnected()
    {
        ThrowIfDisposed();
        var sources = Prune(_contributors);
        var receivers = Prune(_listeners);
        DisconnectedContributors += (ulong)sources;
        DisconnectedListeners += (ulong)receivers;
        return sources + receivers;
    }

    private int Prune(Dictionary<string, NativeVirtualAudioBridge> clients)
    {
        _disconnected.Clear();
        foreach (var (name, peer) in clients)
        {
            if (peer.GetStats().ClosedByOwner != 0)
                _disconnected.Add(name);
        }
        foreach (var name in _disconnected)
            Remove(clients, name);
        var count = _disconnected.Count;
        _disconnected.Clear();
        return count;
    }

    /// <summary>
    /// Nonblocking one-block mix and independent fanout. The caller invokes
    /// Pump at an audio-appropriate cadence. This managed reference code is
    /// not a real-time kernel callback and must not run at elevated IRQL.
    ///
    /// The sample mixer sums concurrent contributions, treats missing frames
    /// as silence, rejects NaN/Inf and hard-clamps to [-1, 1].
    /// </summary>
    public int Pump()
    {
        ThrowIfDisposed();
        // Probe owner HANDLE liveness every 16 blocks, rather than opening
        // Windows objects during every 48 kHz audio tick.
        if ((++_pumpCycles & 15) == 0)
            PruneDisconnected();
        Array.Clear(_mix);
        var producedFrames = 0;
        var channels = (int)Channels;
        foreach (var client in _contributors.Values)
        {
            Array.Clear(_scratch);
            var frames = (int)client.Read(_scratch);
            if (frames > producedFrames)
                producedFrames = frames;
            for (var sample = 0; sample < frames * channels; ++sample)
            {
                var value = _scratch[sample];
                if (float.IsFinite(value))
                    _mix[sample] += value;
            }
        }
        if (producedFrames == 0)
            return 0;
        var sampleCount = producedFrames * channels;
        for (var i = 0; i < sampleCount; ++i)
            _mix[i] = float.IsFinite(_mix[i])
                ? Math.Clamp(_mix[i], -1f, 1f) : 0f;

        // Every listener has an independent SPSC mapping. One slow client
        // drops only its own output. The preallocated buffer is reused even
        // when the last PCM block is shorter than BlockFrames.
        foreach (var client in _listeners.Values)
        {
            var accepted = client.Write(_mix, (uint)producedFrames);
            FramesDelivered += accepted;
            FramesDropped += (ulong)producedFrames - accepted;
        }

        FramesMixed += (ulong)producedFrames;
        return producedFrames;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var client in _contributors.Values)
            client.Dispose();
        foreach (var client in _listeners.Values)
            client.Dispose();
        _contributors.Clear();
        _listeners.Clear();
        _disconnected.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

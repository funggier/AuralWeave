using System.Buffers.Binary;

namespace AuralWeave.Core;

public enum PcmSampleEncoding
{
    Int16,
    Float32
}

/// <summary>
/// Immutable snapshot of the ordered effect instances. Repeated types are permitted,
/// but every instance ID must be distinct. Processing doesn't allocate per audio block.
/// </summary>
public sealed class AudioBlockProcessor
{
    private AudioEffectInstance[] _effects = [];

    public IReadOnlyList<AudioEffectInstance> Effects => Volatile.Read(ref _effects).ToArray();

    public void ReplaceEffects(IEnumerable<AudioEffectInstance> effects)
    {
        var snapshot = effects.ToArray();
        AudioRouteRules.ValidateEffects(snapshot);
        Volatile.Write(ref _effects, snapshot);
    }

    public bool HasProcessing
    {
        get
        {
            foreach (var effect in Volatile.Read(ref _effects))
                if (effect.Enabled && effect.Gain != 1f) return true;
            return false;
        }
    }

    public void Process(Span<byte> audio, PcmSampleEncoding encoding)
    {
        foreach (var effect in Volatile.Read(ref _effects))
            if (effect.Enabled && effect.Gain != 1f)
                ApplyGain(audio, encoding, effect.Gain);
    }

    /// <summary>No allocations; saturation at each stage mirrors effect processing.</summary>
    public static void ApplyGain(Span<byte> audio, PcmSampleEncoding encoding, float gain)
    {
        if (!float.IsFinite(gain) || gain < 0 || gain > 2)
            throw new ArgumentOutOfRangeException(nameof(gain));

        if (encoding == PcmSampleEncoding.Float32)
        {
            if (audio.Length % sizeof(float) != 0)
                throw new ArgumentException("Float32 audio length must be a multiple of four.");
            for (var i = 0; i < audio.Length; i += sizeof(float))
            {
                var sample = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(audio.Slice(i, sizeof(float))));
                sample = Math.Clamp(sample * gain, -1f, 1f);
                BinaryPrimitives.WriteInt32LittleEndian(audio.Slice(i, sizeof(float)),
                    BitConverter.SingleToInt32Bits(sample));
            }
        }
        else if (encoding == PcmSampleEncoding.Int16)
        {
            if (audio.Length % sizeof(short) != 0)
                throw new ArgumentException("PCM16 audio length must be even.");
            for (var i = 0; i < audio.Length; i += sizeof(short))
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(audio.Slice(i, sizeof(short)));
                var adjusted = (short)Math.Clamp(
                    (int)MathF.Round(sample * gain), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(audio.Slice(i, sizeof(short)), adjusted);
            }
        }
        else throw new ArgumentOutOfRangeException(nameof(encoding));
    }

    public static float Peak(ReadOnlySpan<byte> audio, PcmSampleEncoding encoding)
    {
        var peak = 0f;
        if (encoding == PcmSampleEncoding.Float32)
        {
            for (var i = 0; i + 4 <= audio.Length; i += 4)
            {
                var value = Math.Abs(BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(audio.Slice(i, 4))));
                if (float.IsFinite(value)) peak = Math.Max(peak, value);
            }
        }
        else if (encoding == PcmSampleEncoding.Int16)
        {
            for (var i = 0; i + 2 <= audio.Length; i += 2)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(audio.Slice(i, 2));
                peak = Math.Max(peak, Math.Abs((float)sample / 32768f));
            }
        }
        else throw new ArgumentOutOfRangeException(nameof(encoding));
        return Math.Clamp(peak, 0f, 1f);
    }
}

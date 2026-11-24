namespace AuralWeave.Core;

/// <summary>
/// Noise reduction is a distinct stage before the repeatable effects chain.
/// The only bundled RNNoise model is Standard v0.1.1 (not the newer Little model).
/// </summary>
public enum NoiseProcessingMode
{
    Bypass = 0,
    RnnoiseStandard = 1
}

# Vendored RNNoise source for AuralWeave

- Project: Xiph.Org RNNoise
- Version: v0.1.1
- Source: https://github.com/xiph/rnnoise/tree/v0.1.1
- Pinned Git commit: `6cbfd53eb348a8d394e0757b4025c6ded34eb2b6`
- License: BSD 3-Clause; see verbatim `COPYING` in this directory
- Bundled model: standard embedded weights from this tag's `src/rnn_data.c`
- No network downloads are performed by the Windows Build or runtime.

## MSVC compatibility modifications

The v0.1.1 upstream source uses C99 variable-length stack arrays in
`src/pitch.c` and `src/celt_lpc.c`, which Microsoft C does not support.
For those variable-size arrays, the vendored sources use `_alloca` and
`<malloc.h>` to compile under MSVC. The relevant array bounds originate
from RNNoise's internal fixed feature/window sizes.

Only files required by the runtime and license/attribution are included.
The original RNNoise BSD-3-Clause notice must be redistributed in binaries.

## Model formats

The v0.1.1 embedded standard model is **not** interchangeable with arbitrary
weights from current RNNoise versions. Future Regular/Little/Custom choices
require a compatible model ABI, size and independent license evaluation.
This prototype offers only **Standard** as an active model option.

## Signal design

RNNoise expects 48 kHz PCM in 480-sample frames (10 ms).
AuralWeave uses per-route states with a bounded frame adapter and per-channel
state for mono/stereo. Audio in Bypass is never passed through this adapter.
The current resampler is linear interpolation and lacks release-grade
anti-alias filtering, especially when the device runs at 192 kHz.

# ADR 0001 — Route-centric audio graph

- **Date:** 2026-11-24
- **Status:** Accepted for initial design
- **Decision makers:** AuralWeave project planning

## Context

AuralWeave needs physical/virtual capture sources, optional denoising, an extensible effects rack allowing duplicate types, and simultaneous output fan-out. It should remain usable without a kernel driver.

## Decision

1. Each logical route owns one source, one noise slot and an ordered effects-chain array.
2. Each effect list item is a separate stateful instance identified by a stable ID, with no uniqueness constraint on type.
3. A route owns zero or more output branches; endpoint IDs are unique per route.
4. Process shared route DSP once and branch to independent sinks after processing.
5. Separate the low-latency audio plane from UI/control plane and persistent settings.
6. Use physical-device routing first; virtual endpoints stay optional and isolated.
7. Keep application data relative to portable application root where practical.

## Consequences

- Avoids unnecessary repeated AI/noise processing for each output.
- Requires explicit output clock-drift handling and error isolation.
- May eventually require per-output effect chains as an extension, but these are not a requirement for the initial core.
- Allows source, plugin and driver backends to evolve independently.

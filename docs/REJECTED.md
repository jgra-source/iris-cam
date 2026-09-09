# Iris Experiments Not Adopted

Maintainers use this record to avoid repeating approaches that failed the local
delivery goal. Evidence is scoped to the 2026-09-09 tests in `BASELINES.md`, not
to every machine or network.

- **2026-09-09: One outstanding frame.** Serialized browser copying, transfer
  and publication; the 21.33s live trial delivered 14.82fps. Not selected over
  two-frame overlap. This does not prove a regression against the unmatched
  4.2s original sample that reached 26.18fps.
- **2026-09-09: Two credits plus requiring empty `bufferedAmount`.** Prevented
  the intended overlap; 21.71s trial delivered 15.66fps. Removed the empty-socket
  restriction while retaining the two-frame publication-acknowledgment bound.
- **2026-09-09: Registering the next video callback after copying, plus a second
  wall-clock throttle.** Could miss or reject distinct decoded frames. Focused
  regression tests failed before the scheduling correction and passed after it.
- **2026-09-09: Keeping temporary stage telemetry.** Removed after measurement;
  no permanent latency readout was needed for the approved improvement.
- **2026-09-09: Claiming a proven viewer-conflict cause for the final stall.**
  Not established: Chrome still had a connection after the owner closed a
  preview, and only an Iris restart restored delivery. Record the single-viewer
  risk, but do not present it as the demonstrated cause of this incident.
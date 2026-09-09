# Iris Delivery Measurements

This records the evidence behind the 2026-09-09 latency update. Maintainers use
the commands and limits here to compare future changes without mistaking local
frame freshness for camera-to-screen latency.

## Method

Run from the repository root with Iris running and Safari streaming in the
foreground. Do not open another viewer: there is only one viewer slot.

```powershell
.\Tests\sample-frame-status.ps1
```

The sampler makes 21 status requests paced by one-second process CPU samples.
Actual elapsed time determines rates. `AllLive` must be true; interrupted
samples are not performance comparisons. `ageSeconds` starts at `FrameStore.Write`
and is rounded to 10ms. Source stats can remain stale after a disconnect. Endpoint
counter checks catch some resets, not every possible peer restart. CPU is the
Iris process as a percentage of one core, excluding WebView2 and the driver.

## Live Results

Candidate settings: fixed 1280x720 output, phone capture still requests 30fps,
at most two unacknowledged raw frames, publication on receipt, and a 30Hz
placeholder timer when stale. The installed driver is unchanged.

Same PC and phone, sequential observations on 2026-09-09, not randomized trials.
The instrumented candidate was sampled immediately before the original backup.
The final candidate was sampled later, after restarting Iris to recover a stall.

| Build | Seconds | Received fps | Published fps | Source fps | Age median / p95 / max ms | Iris CPU, one core |
|---|---:|---:|---:|---:|---|---:|
| Original installed backup | 23.33 | 10.29 | 30.35 | 30 | 70 / 110 / 140 | 103.4% |
| Candidate, temporary stage timings | 22.95 | 15.68 | 15.68 | 30 | 30 / 90 / 180 | 106.3% |
| Final candidate, timings removed | 22.09 | 17.47 | 17.47 | 30 | 30 / 80 / 100 | 111.5% |

All three samples were live throughout with zero decoder-drop delta. The final
sample decoded 690 frames. Original publication includes repeated pictures;
candidate live publication counts newly received pictures.

These observations support higher delivery and fresher local frames under the
tested conditions. They do NOT establish steady 30fps, reduced CPU, consistent
tail improvement or lower camera-to-screen latency. An earlier 4.2s original
sample reached 26.18fps, demonstrating why short unmatched runs are not a
controlled baseline. Phone orientation and machine load were not pinned by the
sampler. Visual correctness in each target calling app remains a manual check.

## Copy Cost

The fixed 1280x720 frame contains 3,686,400 RGBA bytes. The focused test warms up
10 publications, then times 120 and checks every output pixel:

```powershell
dotnet test Tests/Iris.Tests.csproj -c Release --filter Publish_AtCameraResolution_PreservesPixelsAndReportsCost --logger 'console;verbosity=detailed'
```

Same-session measurements: original scratch conversion plus `WriteArray` took
37.307ms/frame; conversion directly into the mapped slot took 6.506ms/frame,
with zero measured managed allocation per publication after warmup. The test
reports timing but deliberately has no hardware-dependent pass threshold.
Reproducing the original requires its original `SharedFrameBuffer.cs` with the
new measurement test in a separate checkout, not replacing the running app.

Temporary live timings measured publication around 5-7ms and browser drawing
plus pixel readback around 20-27ms. Those timings were removed before the final
build. Browser copying, WebRTC and native NV12 conversion remain potential costs;
none is an end-to-end latency measurement.

## Verification And Recovery

Fresh checks on 2026-09-09:

```powershell
dotnet test Tests/Iris.Tests.csproj -c Release --no-restore -p:NoWarn=MSB3277 -v quiet --logger 'console;verbosity=normal'
node --test Tests/phone-reconnect.mjs Tests/receiver-backpressure.mjs
```

Results: 41 C# tests passed; 15 phone checks and 10 receiver checks passed.
Node reports 11 top-level tests because the phone harness is one file containing
15 internal checks. MSB3277 is the pre-existing WindowsBase assembly warning.
On the test PC the SDK executable is under
`$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe`, not on PATH.

The final candidate initially stopped receiving frames. Reopening the phone
page and tapping Stop/Start did not restore delivery. A Chrome TCP connection
remained alongside WebView2, but TCP ownership does not prove which page or
WebSocket role it held. A restart of only Iris restored the final all-live sample
above, and the owner reported streaming. No root cause or reconnect fix is
claimed. Driver registration was not changed. Longer reconnect and target-app
checks remain open; camera-to-screen latency needs a filmed clock test.

Installed executable: `C:\Tools\Iris.exe`, matching `dist/Iris.exe` at installation.
Final SHA256: `243AE1F48CB665D31B5F547BAE164E6287F22982393FFF61989BC6874EC70A0A`.
Original rollback: `C:\Tools\Iris.rollback-20260909-124812.exe`.
Never restart the camera merely to collect another sample; check current state
first, and do not treat the phone's status text as proof of received frames.
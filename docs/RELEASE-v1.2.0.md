# Iris v1.2.0

This update reduces avoidable copying and buffering between the phone's video
and the Windows camera. It keeps the same 720p output, controls and camera driver.

## Changes

- Publish each received frame immediately instead of waiting for a second timer.
- Convert pixels directly into the shared camera buffer, removing a scratch copy.
- Limit the browser to two frames awaiting publication; skip older pictures when
  busy instead of building a queue.
- Register the next video callback before copying, and avoid throttling distinct
  decoded frames twice.

## Verification

41 C# tests, 15 phone reconnect checks and 10 receiver checks passed.
The final real-phone sample stayed live for 22 seconds: 17.47 received frames
per second, a 30fps source and no decoder drops. Median local frame age was 30ms;
the original build's separate 23-second sample measured 10.29fps and 70ms.

These are short observations on one PC, not guarantees. Local frame age is not
camera-to-screen latency, and lower CPU use has not been demonstrated. See
[measurement details](https://github.com/jgra-source/iris-cam/blob/main/docs/BASELINES.md).

## Known Limits

An intermittent connection stall required restarting Iris during verification;
its cause remains unconfirmed. The browser viewer and hidden receiver still
share one viewer slot, so do not open the browser viewer while using Iris Camera
in a call. Longer reconnect and target-app checks remain open.

## Updating

Quit Iris, replace the existing executable with this release's `Iris.exe`, then
relaunch it. The camera driver has not changed; an existing installation does not
need to be registered again. Keep Safari in the foreground while streaming.
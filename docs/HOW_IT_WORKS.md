# How Iris Works — plain words, standard names, and a self-test

> **What this is:** an explainer for the person who owns this system. Part 1 says how it works
> in plain language. Part 2 gives every concept its standard engineering name. Part 3 is a set
> of questions to answer cold, with the file closed.
> **Why it exists:** `README.md` explains it to a *user*. `CONTINUE-HERE.md` records state and
> traps. Nothing explains the design to an *engineer* — why frames go through a file, why the
> browser does the decoding, and why the picture is deliberately squashed to one size.
> **What breaks without it:** the five time-wasting traps stay a list to memorise instead of
> instances of named problems, and the deliberate decisions get "fixed" by someone helpful.
> **Sources:** `README.md`, `CONTINUE-HERE.md`, `IMPLEMENTATION_PLAN.md` read directly.
> Written 2026-09-05.
> **Standing rule from the owner:** keep it simple and stable. Fixes to things actually broken:
> proceed. Anything that adds surface — a page, a mode, a dependency, a background behaviour —
> describe it in a sentence and ask first. **Prefer the change that deletes code.**

---

# Part 1 — How it works

## What it does

Your iPhone becomes a camera on your Windows PC, over Wi-Fi, with **nothing installed on the
phone**. It appears in Teams, Zoom, Meet and Discord as "Iris Camera", exactly like a USB webcam.

No Mac, no App Store, no account, no paid service anywhere in it.

## The chain

```
iPhone Safari
     │  camera video over Wi-Fi (WebRTC)
     ▼
invisible browser inside the desktop app      ← decodes the video
     │  raw pixels, letterboxed to a fixed 1280x720
     ▼
C:\ProgramData\Iris\frames.bin                ← two slots + a counter
     │
     ▼
camera driver, loaded by Windows INSIDE Teams / Chrome / Zoom
     │
     ▼
"Iris Camera" in the camera list
```

## Three decisions that are not obvious

**The browser does the decoding.** Turning compressed video back into pictures is genuinely hard,
and a browser is already excellent at it. So the app runs one with no window. That avoids shipping
a video codec library **and its licensing** entirely — the second half being the reason people
usually regret the alternative.

**Every frame is squashed into the same size.** A camera has to advertise one resolution and keep
it. Rotating the phone swaps the picture between tall and wide, so frames are **letterboxed with
black bars** instead. The size never changes, and a call does not break mid-way when someone turns
their phone.

**Frames pass through a file, not a named shared-memory object.** This is the decision that cost
hours to learn. Windows loads camera code inside a system service running in **session 0**; the app
runs in **session 1**. A name created by an ordinary program is invisible across that boundary — the
same name means two different objects. **A file path belongs to no session**, so both sides can
simply open it.

## The frame buffer, and why it needs a protocol at all

Two processes touch `frames.bin` at once: the app writes, the driver reads. Without coordination, a
reader can catch a frame half-written and display a picture that is the top of one frame and the
bottom of the next.

The scheme is **two slots plus a counter**: the writer fills the slot the reader is not using and
then advances the counter; the reader checks the counter, reads, and checks again — if it moved, the
read is discarded and retried. No locks, and the writer is never blocked by a slow reader.

That property matters for a camera. **A reader that can block a writer turns a slow consumer into
dropped frames for everyone.**

## The failure mode is a picture, not a freeze

When the app is not running, the camera shows a **"Waiting for iPhone"** image rather than stopping.

The reason is behavioural, not aesthetic: **apps treat a camera that stops sending as broken**, and
some remove it from the list entirely. A camera that keeps producing a frame stays a healthy camera
that happens to have nothing to show — a diagnosis the user can act on.

The cost is a real trap in the other direction: forget to start the app and **the camera still
appears and still shows a picture**, forever. Which is why "Start when I sign in" is a first-class
menu item.

## The certificate architecture: Root CA hierarchy and iOS Trust Profiles

Browsers only allow camera access over `https`. Encryption needs a certificate. Trusted certificates
are issued for public domain names, and this runs on a home network with no domain name.

Iris solves this by generating a local **Certificate Authority (Root CA)** hierarchy:
- A persistent Root CA (`CN=Iris Camera Local CA`) is generated and stored locally in `%LocalAppData%\Iris\iris-ca.pfx`.
- The server certificate is signed by this Root CA and renewed if the machine's local IP changes.
- Iris serves an Apple Configuration Profile at `/ca.mobileconfig` and a setup page at `/install-ca.html`.

Users can either:
1. Tap through the standard Safari 2-tap bypass for casual use.
2. Install the iOS trust profile once to permanently eliminate all security warnings in Safari.

The traffic is encrypted and never leaves the local network.

## Five things that will waste your time

1. **The buffer path is written in two places** — once in C#, once hard-coded in the C++ header —
   and matched only by convention. If they disagree, the camera **silently shows moving colour
   bands** instead of your phone. The fallback is deliberate, but it makes a configuration mismatch
   look like a mystery rather than an error. *This is now closed by a test that reads the C++ header
   as text and compares its offsets, magic number and path against the C#.*
2. **Never use a `Local\` named object to reach the driver.** Session 0 versus session 1 — the same
   name is two different things. This is why it is a file.
3. **The driver must live where `NT AUTHORITY\LocalService` can read it** — Program Files, not a
   user profile. From a profile, creation *appears to succeed* and then start fails with access
   denied. **A two-stage failure where stage one reports success is the expensive kind.**
4. **`regsvr32` does nothing here.** The DLL has no `DllRegisterServer`. Registration is one
   registry key.
5. **The camera service holds the DLL open.** To replace it you must remove the camera *first*, then
   stop the service, then copy. Stopping the service while the camera still exists just makes it
   reload the file.

## Deliberate decisions — do not "fix" these

- **The picture is not mirrored.** Real webcams send unmirrored; apps mirror your own preview for
  you. The side effect is that the placeholder text reads backwards **in your own preview only** —
  it is correct for everyone else, and a camera cannot know whether an app will mirror it.
- **Capture is 1280×720, not 1080p.** Everything is squashed to 720p anyway, and asking for pixels
  that get discarded made the phone **drop frame rate to protect resolution** — 1080p at 14fps
  instead of 720p at 30. Requesting more than you use is not free.
- **The offscreen browser window is positioned off-screen, not hidden.** Browsers throttle rendering
  for windows they believe nobody can see. Hidden means slow; off-screen means full speed.
- **The console is hidden only on double-click**, not when launched from a terminal — hiding it
  there would take the user's own window with it, and they are usually watching output deliberately.
- **Autostart uses the per-user Run key**, not a Startup shortcut: no administrator needed, and
  Windows lists it under Startup in Task Manager so users can disable it the normal way. It records
  the executable's current path, so **moving the file quietly breaks it.**

## Packaging: one file, and the one thing packaging cannot solve

A single `Iris.exe` carries the web pages, the camera driver and its registration tool.

**But the driver cannot run from inside the executable.** Windows loads it *by path*, into other
applications' processes, so it must exist as a real file on disk. That is why `--install` is a
separate step — and it is not something better packaging can remove.

**Build order matters and is not obvious:** driver, then tool, then the app that embeds them. Build
the app first and you get an app that cannot install a camera, **with no obvious sign why**.

## The tests, and how they were proven non-vacuous

36 tests, run with only the .NET SDK — no camera install, no C++ build, and nothing touching the
live buffer.

Three areas: the shared buffer (header layout, colour conversion, slot alternation, short frames,
plus a **writer thread racing a reader** that checks every frame is one flat colour — a torn read
would show two), the frame store one layer up, and a **driver contract test** that reads the C++
header as text and compares it against the C#.

Two things worth carrying forward:

- **They found a real bug.** `Publish` kept its scratch buffer per *thread* rather than per
  *instance*, and reused it without checking length. Two buffers of different sizes on one thread
  would run off the end of the array. It **never fires today** — one buffer, one fixed size — but
  would the moment resolution became adjustable. *A latent bug behind a current invariant.*
- **They were verified by mutation.** Single-buffering the writer, removing the colour swap, and
  shifting a header offset were each introduced deliberately, and each failed exactly the tests that
  should have failed. **A test suite nobody has watched fail is not known to work.**

And the phone's reconnect logic is tested separately with the browser stubbed out, so a real iPhone
does not have to be locked and unlocked repeatedly.

## The reconnect work, and the bug it exposed

iOS suspends the camera when the phone locks or Safari leaves the front. Reconnection uses backoff,
a fresh peer connection each time, camera re-acquisition on track end, and an immediate retry on
visibility or focus — **but only when something is actually broken**, which is what stops it
thrashing on every tab switch.

Fixing it exposed a race that had always existed: **the server cleared its socket registration
unconditionally when a handler ended**, so a fast reconnect had its live registration wiped by the
dying old handler. Rare before, routine afterwards.

**That is the general shape: making something faster turns a rare race into a reliable one.** The
race was not introduced by the reconnect work; it was revealed by it.

---

# Part 2 — Concept glossary

### Inter-process communication

**Session isolation (Windows session 0).** Services run in a separate session from user
applications. Kernel object names created in one are invisible to the other. The reason a named
shared-memory object cannot bridge app and driver here.

**Named kernel object namespaces (`Local\` vs `Global\`).** Which session a name belongs to.
Getting this wrong produces two objects with one name and no error.

**File-backed shared memory.** Using a filesystem path — which belongs to no session — as the
rendezvous. Slower in theory, correct in practice, and vastly simpler.

**Seqlock (sequence lock).** A lock-free reader/writer scheme where the writer bumps a counter
before and after writing and the reader retries if it changed mid-read. Writers never block; readers
may retry. Ideal when the writer must never stall.

**Double buffering / slot alternation.** Writing into the slot the reader is not using, so a reader
never sees a partially-written frame.

**Torn read.** A read that catches a write in progress and returns a mix of two states. What the
"every frame is one flat colour" test detects.

**Lock-free vs blocking.** A blocking reader converts a slow consumer into dropped frames for the
producer. For a live stream, dropping is the correct failure and blocking is not.

### Contracts across languages

**Binary layout contract / ABI.** Two languages agreeing on offsets, sizes and a magic number.
Nothing enforces it at compile time when the two sides build separately.

**Contract test.** A test that reads the *other* language's header as text and asserts agreement.
The mechanical replacement for a convention.

**Convention as a failure mode.** Two hard-coded copies of one path, correct only because someone
remembers. Its failure is silent — a fallback picture rather than an error.

**Hand-ported implementation with a drift guard.** The C++ reader copied into C# so tests run
without a C++ build, plus a test that fails if the copy diverges.

### Media

**WebRTC.** Peer-to-peer real-time media in the browser. Why no app is needed on the phone.

**Codec licensing avoidance.** Delegating decode to a component you already ship rather than
embedding a video library and inheriting its patent obligations.

**Letterboxing / fixed-format output.** Padding to a constant resolution because a camera device
must advertise one format and keep it. Renegotiating mid-call breaks consumers.

**Resolution/frame-rate trade.** Requesting pixels you discard costs frame rate — the phone protects
resolution at the expense of smoothness. Asking for more than you need is not free.

**Render throttling of invisible windows.** Browsers slow drawing for windows they think nobody can
see. Off-screen positioning avoids the heuristic; hiding triggers it.

**Placeholder frame instead of stream stop.** Keeping the device "healthy" for consumers that treat
silence as failure. Trades one confusion (a stale picture) for a worse one (a disappearing device).

### Failure behaviour

**Silent fallback / degraded default.** Colour bands when the buffer cannot be read. Deliberate, but
it converts an error into a mystery — the cost of every silent fallback.

**Two-stage failure with a successful stage one.** Creation succeeds from a user profile and start
fails with access denied. The most expensive shape, because the first result misdirects you.

**Latent bug behind an invariant.** Per-thread buffer reuse that cannot fire while one fixed size
exists. Correct today, wrong the moment a constraint relaxes.

**Race revealed by speed.** Making reconnection fast turned an always-present registration race from
rare into routine.

**Mutation testing.** Deliberately breaking the code to confirm the tests notice. The only proof a
suite is not vacuous.

### Distribution

**Single-file deployment, and its limit.** Everything embedded except the thing the OS must load by
path. Packaging cannot remove a requirement imposed by the loader.

**Build-order dependency.** Driver, tool, then the embedding app. The wrong order produces a working
build that is missing a capability, with no error.

**Code signing absence.** Unsigned binaries trigger SmartScreen. Explicitly excluded from scope, and
the README tells users what they will see.

**Self-signed TLS on a LAN.** Encryption without a trusted authority, because there is no public
domain name. The warning is expected; documenting it in advance turns an alarm into a step.

**Per-user Run key vs Startup folder.** No administrator required, and visible in Task Manager's
Startup tab where users already know to look. Records an absolute path, so moving the file breaks
it silently.

---

# Part 3 — Self-test

**The chain**

1. Trace a single frame from the phone's sensor to a Teams window, naming each hop.
2. Why does the app run an invisible browser instead of decoding video itself? Give both reasons.
3. Why are frames letterboxed to a fixed size rather than passed through at their real dimensions?

**The file**

4. Why is `frames.bin` a file rather than a named shared-memory object? Name the Windows concept.
5. Describe the two-slot-plus-counter scheme, and say what the reader does when the counter moves
   mid-read.
6. What is the standard name for that scheme, and why does it suit a camera specifically?
7. What does "every frame is one flat colour" actually test for?

**Failure behaviour**

8. The app is not running. What does the camera show, and why is that better than stopping?
9. What is the cost of that choice, and which menu item exists because of it?
10. The camera shows moving colour bands. What has gone wrong, and why does it look like a mystery
    rather than an error?

**The five traps**

11. The driver is installed from a user profile. What happens, and why is that shape expensive?
12. Why does `regsvr32` do nothing here?
13. You need to replace the driver DLL. Give the order of operations and say what happens if you get
    it wrong.

**Deliberate decisions**

14. Why is 1280×720 chosen over 1080p? Give the measured consequence.
15. Why is the offscreen window positioned off-screen rather than hidden?
16. Why is the picture not mirrored, and what visible oddity does that cause?

**Tests**

17. A bug was found that cannot fire today. What was it, and under what future change would it fire?
18. How was the test suite proven non-vacuous? Name the technique and give one mutation used.
19. What does the driver contract test read, and which trap does it close?

**Packaging**

20. Why can the driver not run from inside `Iris.exe`?
21. Build the app before the driver. What do you get, and how would you notice?

**The one that matters most**

22. Someone asks you to add a settings page so users can pick the resolution. Using the owner's
    standing rule, what is your answer — and which existing latent bug does that request activate?

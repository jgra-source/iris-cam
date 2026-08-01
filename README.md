# iPhone Webcam

Use your iPhone as a high-quality camera on your PC, over Wi-Fi, with no app to install on the phone.

No Mac needed. No App Store. No account. Free.

## What it does

Your iPhone becomes a camera that Windows knows about. Open **Teams**, **Zoom**,
**Google Meet** or **Discord**, look in the camera list, and pick **iPhone Webcam** —
exactly as you would pick a USB webcam.

You can also just watch the picture on your PC in a browser, without installing
the camera at all.

### Two parts, and you may only want the first

| | What you get | Install needed |
|---|---|---|
| **The app** | iPhone video on your PC screen, in a browser | None beyond running it |
| **The camera** | "iPhone Webcam" in Teams, Zoom, Meet, Discord | One-time, needs administrator |

The camera part is Windows-only. The app part works anywhere.

## What you need

- A Windows PC and an iPhone **on the same Wi-Fi network**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on the PC
- iOS 14.3 or newer (any iPhone from the last several years)

## Running it

From the `Windows-Client` folder:

```
dotnet run
```

The program prints the addresses to use. It looks something like this:

```
  On your PC browser (viewer):
      https://localhost:9443/viewer.html

  On your iPhone (Safari, same Wi-Fi network):
      https://192.168.1.42:9443
```

The iPhone address is detected automatically from your network, so it will not match the example above.

Then:

1. **On your iPhone**, open the address **in Safari**.
2. Tap **Start Camera** and allow camera access.
3. To watch on your PC, open the viewer address in Chrome or Edge.

The order does not matter — whichever connects second is told about the first.

## Installing the camera (optional)

Only needed if you want **iPhone Webcam** to appear inside Teams, Zoom, Meet and
Discord. Skip it if you are happy watching in a browser.

1. Build the driver in `Virtual-Camera-Driver/` (Release, x64). Needs the free
   *Build Tools for Visual Studio* with the C++ workload and the Windows 11 SDK.
2. Right-click `Virtual-Camera-Driver\install-camera.cmd` → **Run as administrator**.

Administrator rights are required because adding a camera to Windows is a
system-wide change. The installer copies the driver to `Program Files`, registers
it, and creates the device.

To replace it after rebuilding: `update-camera.cmd` (as administrator).
To remove it entirely: `register-camera.exe /uninstall`, then delete the
registry key and `C:\Program Files\iPhoneWebcam`.

**The app must be running** for the camera to show your phone. When it is not,
the camera shows a "Waiting for iPhone" picture rather than freezing — apps
dislike a camera that stops sending anything.

## The security warning is expected

Both browsers will warn that the connection is not private, and Safari will say something like *"This Connection Is Not Private"*.

**This is normal and it is not a sign that something is broken.**

Browsers only allow camera access over an encrypted (`https`) connection. Encryption needs a certificate. Certificates that browsers trust automatically are issued for public domain names, and this program runs on your home network with no domain name — so it creates its own certificate instead. Browsers cannot verify a certificate that a program made for itself, so they warn you.

The traffic is still encrypted, and it never leaves your local network.

To continue on iPhone:

1. Tap **Show Details**
2. Tap **visit this website**
3. Tap **Visit** to confirm

On Chrome or Edge on the PC: click **Advanced**, then **Proceed**.

You only need to do this once per device, unless the certificate is regenerated.

## Controls on the phone

| Button | What it does |
|---|---|
| **Start Camera** | Begins capture and connects to the PC |
| 🔄 | Switches between the front and back camera |
| ↻ | Rotates the picture 90° each tap — use this when the phone is lying on its side |
| **Stop** | Ends the stream |

The rotate button turns the video that is actually sent, not just the preview on the phone. So the picture arrives the right way up on the PC.

## The numbers under the video

The PC viewer shows resolution, frame rate, codec and bitrate. These come from the browser's own counters and describe what genuinely arrived, which makes them useful for spotting a weak Wi-Fi connection.

## If it does not work

**The iPhone address will not load.**
Check both devices are on the same Wi-Fi. Phones sometimes sit on a "guest" network that blocks device-to-device traffic. Windows Firewall may also be blocking the port — allow the app when prompted.

**The page loads but the camera does not start.**
Camera access requires `https`. Make sure you did not change the address to `http`.

**"Live" shows on the PC but the picture is black.**
Reload the viewer page. If it persists, open the browser console (F12) and check for errors.

**The picture freezes, or the camera goes back to "Waiting for iPhone".**
iOS shuts the camera off when the phone locks or you switch apps. Keep the phone
awake with the page in front. Reopening it reconnects.

**"iPhone Webcam" shows moving colour bands instead of my camera.**
The driver cannot read the app's frames. Check the app is running, and that
`C:\ProgramData\iPhoneWebcam\frames.bin` exists.

**"iPhone Webcam" is missing from an app's camera list.**
Some apps only look for cameras at startup. Close it fully and reopen.

## How it works

```
iPhone Safari
     │  camera video over Wi-Fi (WebRTC)
     ▼
Invisible browser inside the app   ← decodes the video
     │  raw pixels, letterboxed to a fixed 1280x720
     ▼
C:\ProgramData\iPhoneWebcam\frames.bin   ← a file both sides can reach
     │
     ▼
Camera driver, loaded by Windows inside Teams / Chrome / Zoom
     │
     ▼
"iPhone Webcam" in the camera list
```

A few decisions worth knowing, because they are not obvious:

**The browser does the decoding.** Turning compressed video back into pictures is
difficult and the browser is already excellent at it, so the app runs one with no
window. That avoids shipping a video library and its licensing entirely.

**Every frame is squashed into the same size.** A camera has to advertise one
resolution and keep it. Rotating the phone swaps the picture between tall and
wide, so frames are letterboxed with black bars instead — the size never changes
and calls do not break mid-way.

**Frames pass through a file, not shared memory with a name.** Windows loads
camera code inside a system service that runs in a different session, where names
created by an ordinary program are invisible. A file path belongs to no session,
so both sides can simply open it.

## Project layout

| Folder | What is in it |
|---|---|
| `Windows-Client/` | The desktop app: serves the pages, runs the invisible browser, publishes frames |
| `Windows-Client/wwwroot/` | `index.html` (phone), `viewer.html` (watch on PC), `receiver.html` (invisible), `camera-check.html` (lists cameras) |
| `Virtual-Camera-Driver/` | The camera driver and its installer. Derived from Microsoft's sample — see `THIRD-PARTY-NOTICES.md` |
| `iOS-Broadcaster/` | Abandoned native Swift approach, kept for reference. Needs a Mac; not used. |

## Cost

Nothing. Every piece of this is free, and there is no paid service, subscription or developer account anywhere in it.

## Known limitations

- **iOS suspends the camera** when the phone locks or Safari is not in front.
  The phone has to stay awake with the page open.
- **The picture is not mirrored.** That matches how every real webcam behaves —
  Teams and Meet mirror your own preview for you, and the people you are talking
  to see you the right way round.
- **Binaries are unsigned**, so Windows shows a warning if you distribute them.
  Building from source avoids this.

## Status

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for the full build history,
including what was tried, what failed, and why each decision was made.

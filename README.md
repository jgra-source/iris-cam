# Iris

Use your iPhone as a high-quality camera on your PC, over Wi-Fi, with no app to install on the phone.

No Mac needed. No App Store. No account. Free.

## What it does

Your iPhone becomes a camera on your Windows PC.

You run one program on the PC. Your phone sends its camera to it over Wi-Fi,
using nothing but Safari — there is no app to install on the phone.

**What you do with that picture is up to you:**

- **Watch it in a browser on your PC.** Works as soon as the program is running.
- **Use it in Teams, Zoom, Google Meet or Discord**, where it appears in the
  camera list as **Iris Camera**, exactly like a USB webcam. This needs a
  one-time install, run as administrator.

The program is what receives your phone's video, so it has to be running either
way — including while you are in a call. When it is not running, the camera shows
a "Waiting for iPhone" picture rather than freezing.

**Windows only.** The program uses Windows components for the camera and the
built-in browser. The only thing that works on any device is the phone side:
any iPhone with Safari, nothing installed.

## Getting it

Download **Iris.exe** from
[Releases](https://github.com/jgra-source/iphone-webcam/releases). One file,
nothing to unzip, no runtime to install first.

You need a Windows PC and an iPhone **on the same Wi-Fi network**, and iOS 14.3
or newer (any iPhone from the last several years).

Windows will warn that the publisher is unknown, because the file is not
code-signed. Choose **More info** then **Run anyway**, or build it yourself from
source if you would rather not take that on trust.

## Using it

Run `Iris.exe`. It prints the addresses to use:

```
  On your PC browser (viewer):
      https://localhost:9443/viewer.html

  On your iPhone (Safari, same Wi-Fi network):
      https://192.168.1.42:9443
```

The iPhone address is detected from your network, so it will not match the
example above.

1. **On your iPhone**, open that address **in Safari**.
2. Tap **Start Camera** and allow camera access.
3. To watch on your PC, open the viewer address in Chrome or Edge.

The order does not matter — whichever connects second is told about the first.

### It lives in the notification area

There is no window to keep on screen. The program sits by the clock as a small
camera icon: **grey** when it is waiting, **green** when your phone is sending.

Windows hides new icons to begin with, so click the **^** on your taskbar to find
it. Drag it out onto the taskbar to keep it in view.

Right-click it for: the phone address (click to copy), **Watch on this PC**,
**Start when I sign in**, and **Quit**. Quit stops the camera — that is the only
way it should ever stop.

Run with `--console` if you would rather have the old console window and watch
what it is doing.

## Adding the camera to Teams, Zoom and Meet

Only needed if you want **Iris Camera** in those apps' camera lists. Skip it if
watching in a browser is enough.

Right-click `Iris.exe`, **Run as administrator**, then:

```
Iris.exe --install
```

Administrator rights are required because adding a camera to Windows is a
system-wide change. To remove it later: `Iris.exe --uninstall`.

## After a restart

The camera stays installed permanently — you never need to install it twice.
**The program does not restart itself**, though, and that fails in a confusing
way: the camera still appears in Teams and still shows a picture, just the
"Waiting for iPhone" one, forever, because nothing is feeding it.

So either run `Iris.exe` again after each restart, or tick **Start when I
sign in** in the tray icon's menu — same as running:

```
Iris.exe --autostart
```

No administrator needed. Turn it off from the same menu, with `--autostart-off`,
or from the **Startup** tab in Task Manager like any other program.

Put the file somewhere permanent before setting this. It records where the
program currently is, so moving or renaming it afterwards quietly stops it
working.

Either way, **leave it running while you are on a call** — it is what receives
your phone's video.

## Building it yourself

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and —
for the camera half — *Build Tools for Visual Studio* with the C++ workload and
Windows 11 SDK 10.0.26100. Both free.

```
build-release.cmd
```

That produces `dist\Iris.exe`. To run the app alone without building the
driver: `cd Windows-Client && dotnet run`.

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

**"Iris Camera" shows moving colour bands instead of my camera.**
The driver cannot read the app's frames. Check the app is running, and that
`C:\ProgramData\Iris\frames.bin` exists.

**"Iris Camera" is missing from an app's camera list.**
Some apps only look for cameras at startup. Close it fully and reopen.

## How it works

```
iPhone Safari
     │  camera video over Wi-Fi (WebRTC)
     ▼
Invisible browser inside the app   ← decodes the video
     │  raw pixels, letterboxed to a fixed 1280x720
     ▼
C:\ProgramData\Iris\frames.bin   ← a file both sides can reach
     │
     ▼
Camera driver, loaded by Windows inside Teams / Chrome / Zoom
     │
     ▼
"Iris Camera" in the camera list
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

## Licence

MIT — see [LICENSE](LICENSE).

The camera driver is derived from Microsoft's Windows-Camera VirtualCamera
sample, also MIT. See
[Virtual-Camera-Driver/THIRD-PARTY-NOTICES.md](Virtual-Camera-Driver/THIRD-PARTY-NOTICES.md)
for that notice and a list of what was changed.

## Cost

Nothing. Every piece of this is free, and there is no paid service, subscription or developer account anywhere in it.

## Known limitations

- **iOS suspends the camera** when the phone locks or Safari is not in front.
  The phone has to stay awake with the page open.
- **The picture is not mirrored.** That matches how every real webcam behaves —
  Teams and Meet mirror your own preview for you, and the people you are talking
  to see you the right way round. A side effect: the "Waiting for iPhone" text
  reads backwards in your own preview. It is the right way round for everyone
  else, and a camera cannot tell whether an app will mirror it.
- **Binaries are unsigned**, so Windows shows a warning if you distribute them.
  Building from source avoids this.

## Status

Working and in use. Built and verified against Chrome, Google Meet and Microsoft
Teams on Windows 11.

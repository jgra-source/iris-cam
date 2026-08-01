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

## Setup

Five minutes, once.

### 1. Download it

Get **Iris.exe** from [Releases](https://github.com/jgra-source/iris-cam/releases).
One file. Nothing to unzip, no runtime to install first.

**Put it somewhere permanent** — `C:\Tools\Iris.exe` or similar, not Downloads.
Step 5 remembers where it is, and moving the file later quietly breaks it.

### 2. Add the camera to Windows

Open the folder where you put it. Click in the **address bar** at the top, type
`cmd`, and press Enter — that opens a terminal already in that folder, with no
typing of paths.

Then run:

```
Iris.exe --install
```

Two prompts to expect, both normal:

- **"Windows protected your PC"** — the file is not code-signed. Click
  **More info** → **Run anyway**.
- **A permission prompt** — adding a camera changes the whole system, so it
  needs administrator rights. Iris asks for them itself; just approve it.

*Only watching on your PC and not using Teams or Zoom? You can skip this whole
step.*

### 3. Connect your phone

Double-click `Iris.exe`. No window opens. It starts in the notification area, by
the clock, and shows you the address your phone needs:

```
  On your iPhone (Safari, same Wi-Fi network):
      https://192.168.1.42:9443
```

Yours will be different — it is read from your own network. If you miss the
notification, right-click the Iris icon by the clock; the address is on the
menu, and clicking it copies it.

Open that address **in Safari on your iPhone** and tap **Start Camera**.

Safari will warn *"This Connection Is Not Private"*. **That is expected**, and
[here is why](#the-security-warning-is-expected). Tap **Show Details** → **visit
this website**.

### 4. Pick it in your video app

In Teams, Zoom, Google Meet or Discord, choose **Iris Camera** from the camera
list — the same place you would pick a USB webcam.

### 5. Make it survive a restart

Right-click the Iris icon by your clock and tick **Start when I sign in**.

Without this you have to start Iris by hand after every reboot — and forgetting
is confusing, because the camera still appears in Teams and still shows a
picture, just a "Waiting for iPhone" one, forever.

---

**Requirements:** a Windows PC and an iPhone **on the same Wi-Fi network**, and
iOS 14.3 or newer (any iPhone from the last several years).

**Leave Iris running while you use the camera.** It is what receives your phone's
video. Closing it stops the camera.

## Day to day

**It lives by the clock**, not in a window. A small camera icon: **grey** while
waiting, **green** once your phone is sending.

Windows hides new icons at first, so click the **^** on your taskbar to find it.
Drag it out onto the taskbar to keep it in view.

Right-click it for the phone address (click to copy), **Watch on this PC**,
**Start when I sign in**, and **Quit**.

**Each time you want to use it:** open the address in Safari and tap Start
Camera. Iris itself keeps running in the background.

**Hold the phone sideways** — landscape fills the whole frame. Upright leaves
black bars down both sides.

## Put it on your iPhone home screen

Typing the address every time gets old. Save it once and it becomes an icon you
tap, like an app.

1. Open the address in **Safari** on your iPhone
2. Tap **Share** — the square with an arrow pointing up
3. Scroll down and tap **Add to Home Screen**
4. Name it **Iris**, then tap **Add**

Tapping that icon now goes straight to the camera page. Tap **Start Camera** and
you are streaming.

It still opens in Safari, and that is on purpose: the certificate notice and the
camera permission both need Safari to work properly.

**One thing to know.** The shortcut remembers your PC's address on your network.
That address usually stays the same for weeks, but it can change when your router
restarts. If the icon stops loading one day, that is why — check the current
address by right-clicking the Iris icon next to your clock, and save the shortcut
again.

## Removing it

```
Iris.exe --uninstall
```

Approve the permission prompt. That takes the camera out of Windows and deletes
what was installed. Then delete `Iris.exe` itself.

## Commands

```
Iris.exe                   run it
Iris.exe --install         add "Iris Camera" to Windows
Iris.exe --uninstall       remove it
Iris.exe --autostart       start when signing in
Iris.exe --autostart-off   stop doing that
Iris.exe --console         open a console and watch what it is doing
```

## Building it yourself

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and —
for the camera half — *Build Tools for Visual Studio* with the C++ workload and
Windows 11 SDK 10.0.26100. Both free.

```
build-release.cmd
```

That produces `dist\Iris.exe`. To run the app alone without building the
driver: `cd Windows-Client && dotnet run`.

To run the tests: `dotnet test Tests`. They need only the .NET SDK — no camera
install, no C++ build, and nothing touches the live
`C:\ProgramData\Iris\frames.bin`.

The phone page's reconnect logic is checked separately, with the browser stubbed
out so a real iPhone need not be locked and unlocked repeatedly:

```
node Tests/phone-reconnect.mjs
```

That one needs Node, but nothing is installed for it and nothing else depends on
it.

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
iOS shuts the camera off when the phone locks or you switch apps. Bringing
Safari back to the front reconnects on its own, usually within a second. If the
status bar turns orange, tap it — that happens when iOS will not hand the camera
back until the page is in front.

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
| `Tests/` | Checks on the frame handling, including that the C# and the C++ still agree on the shared layout |

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
  Bringing the page back to the front reconnects automatically, but the stream
  does stop while the phone is away — so the phone still has to stay awake with
  the page open for an uninterrupted call.
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

# iPhone Webcam

Use your iPhone as a high-quality camera on your PC, over Wi-Fi, with no app to install on the phone.

No Mac needed. No App Store. No account. Free.

## What this is right now

You run a small program on your PC. It gives you a web address. You open that address in Safari on your iPhone, tap Start, and your phone's camera appears on your PC screen.

**It does not yet appear as a camera inside Zoom, Teams or Meet.** That part is planned but not built — see [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

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

1. **On your PC**, open the viewer address in Chrome or Edge. You should see "Waiting for iPhone…".
2. **On your iPhone**, open the other address **in Safari**.
3. Tap **Start Camera** and allow camera access.
4. Your camera should appear on the PC within a second or two.

Open the PC viewer first. It signals that it is ready, which is what tells the phone to start sending.

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

**Nothing happens after tapping Start.**
Open the PC viewer page first, then reload the page on the phone. The phone waits for a viewer to be present before it starts sending.

## How it works

```
iPhone Safari  ──WebRTC video──▶  PC browser
      │                                │
      └────── handshake only ──────────┘
                    │
            C# server on the PC
      (serves the pages, introduces the two
       sides to each other, then steps out)
```

The video travels directly from the phone to the PC browser. The C# server only helps them find each other at the start — it never sees or handles the video itself.

## Project layout

| Folder | What is in it |
|---|---|
| `Windows-Client/` | The C# program: serves the pages and handles the handshake |
| `Windows-Client/wwwroot/` | `index.html` (phone camera page), `viewer.html` (PC page) |
| `Virtual-Camera-Driver/` | Early sketch of the future virtual camera. **Not working yet.** |
| `iOS-Broadcaster/` | Abandoned native Swift approach, kept for reference. Needs a Mac; not used. |

## Cost

Nothing. Every piece of this is free, and there is no paid service, subscription or developer account anywhere in it.

## Status and what comes next

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for the full plan, including the work needed to make this appear as a real camera device in Zoom, Teams and Meet.

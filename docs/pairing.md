# Run ChildStream and pair a client

Pairing has two different addresses. Use the **Sunshine web UI** in a browser on
your normal PC desktop, and the **Client address** in Moonlight on the other device.
You do not need Google Chrome inside the child desktop to configure Sunshine.

## First installation

1. Extract the complete portable ZIP into a permanent, private folder. Do not run
   the executable inside the ZIP or copy only the EXE into a new folder.
2. As the Windows user who will run it, open administrator PowerShell in that
   extracted folder and run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup.ps1
   ```

   Setup downloads portable Sunshine and changes Windows child-session, RDP,
   compositor, minimized-rendering, firewall and startup settings. Do not rerun it
   just to troubleshoot a browser page on an otherwise working installation.
3. Open `ChildStream.exe`, select **Start desktop** and enter your Windows account
   password, not a Windows Hello PIN. Wait for **Connected**.
4. If asked, choose whether to start Sunshine. Handle any Windows elevation prompt
   inside the child desktop yourself. Wait for **Sunshine running**.

## Open the correct Sunshine web UI

1. Leave the child desktop attached. On your **normal PC desktop**, open Chrome,
   Edge or another browser that works there.
2. Copy the launcher's **Sunshine web UI** URL into that browser. For the new-install
   base port 48989, the URL is:

   ```text
   https://127.0.0.1:48990/
   ```

   `127.0.0.1` means this host PC. It is not an address to enter on your phone.
   Port 47990 normally belongs to a separate default-port console Sunshine host.
   Always use the child instance's actual displayed port.
3. Sunshine uses a self-signed certificate. If your browser shows a privacy warning,
   check that you entered the exact local address above and review/handle the
   warning yourself. Do not disable certificate checks globally.
4. On first use, create a **Sunshine web username and password**. These are separate
   from your Windows password and Moonlight PIN. Keep them private. If a browser
   authentication box appears, use that Sunshine web account, not your Windows PIN.
5. **Open Sunshine settings** requests your Windows default browser. Some browser
   handlers may exit or reuse another window without showing it; the visible URL
   and **Copy settings URL** are the manual fallback. **Show pairing steps** gives
   the same sequence inside the launcher.

## Pair Moonlight or a compatible client

1. Put the host PC and client on the same LAN for this first test.
2. In Moonlight on your phone/other computer, choose **Add PC** (the plus button)
   and enter the launcher's Client address, normally `PC-NAME:48989`.
3. If the PC name does not resolve, use its active LAN IPv4 address with the same
   port. Find the address in Windows network properties or `ipconfig` on the host.
   For example, if the host's LAN address is `192.168.1.50`, enter
   `192.168.1.50:48989`. That IP is an example, not a fixed ChildStream address.
4. Select that host in Moonlight. It displays a short pairing PIN.
5. Back in the **normal-desktop browser**, open Sunshine's **PIN** tab. Enter the
   PIN currently displayed by Moonlight and a device name, then submit.
6. After Sunshine reports successful pairing, return to Moonlight and choose
   **Desktop**. Start a game from that streamed desktop.
7. Keep ChildStream running and attached; use **Minimize to tray**. Test video,
   audio and keyboard/mouse before adding controller/codec/refresh complexity.

## If Chrome will not open inside the child desktop

Pair through the normal-desktop browser first. Child sessions use the same Windows
user, and browsers may conflict when two sessions try to use the same profile.
This is separate from Sunshine's pairing and capture paths.

If you need Chrome for applications inside the child, create a separate Chrome
shortcut **inside the child desktop** and give it its own profile directory:

```text
"C:\Program Files\Google\Chrome\Application\chrome.exe" --user-data-dir="%LOCALAPPDATA%\ChildStream\Chrome-Child"
```

Adjust the Chrome executable path if installed elsewhere. Use that profile only in
the child desktop; keep the normal desktop on its usual profile. Do not copy an
active Chrome profile or close every Chrome process to make pairing work. The
[Chromium profile documentation](https://www.chromium.org/developers/creating-and-using-profiles)
describes independent `--user-data-dir` instances. This is a troubleshooting option,
not a claim that every Chrome failure is a profile conflict.

## Updating an existing installation

Finish/save child applications first. End the child session only when you are ready
to close its applications, then exit the viewer and any remaining idle
`ChildStream.exe -agent` processes. Extract updated files over the same install
folder, preserving `Sunshine`, `cred.bin` and `display.cfg`. Never replace those
runtime files with someone else's copy. Reopen ChildStream and start the desktop;
the startup hook starts the helper in the new session. Existing Sunshine settings
and paired devices remain in the preserved Sunshine folder.

The browser/pairing-UI update does not need Windows setup rerun when the existing
helper and setup already work. If Windows locks the EXE, a launcher/helper is still
running: close it before replacing the file. Do not change the installation folder
without updating its startup/firewall paths through setup.

## What a successful test establishes

The launcher saying **Sunshine running** proves the expected host process exists
inside the child; it does not prove pairing, audio, capture or streaming health.
If the web page is unreachable, check `launcher.log`, `autostart.log` and
`Sunshine\Sunshine\config\sunshine.log`. Never publish credentials or the Sunshine
state file. Record the first failing step and error message before changing ports,
drivers or encoder settings.

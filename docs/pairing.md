# Run Annex and pair a client

The **Host web UI** URL belongs in a browser on the host PC's normal desktop.
The **Client address** belongs in Moonlight or another compatible client on the
other device. Chrome inside the child desktop is optional for pairing.

## Start the desktop

1. Keep `Annex.exe` in a permanent, private, writable installation folder.
   On this development PC the installed copy is
   `C:\Users\Admin\Downloads\Annex-portable\Annex.exe`.
   Repository/dist EXEs are build outputs, not that installation's helper.
2. Open the installed EXE. For a new Windows installation, select **Setup / repair**
   and handle elevation yourself. Setup is contained in the EXE; source files,
   ZIPs and a PowerShell terminal are unnecessary. It enables child sessions, sets
   RDP/compositor preferences and registers an idle helper. It downloads no host.
3. Choose **Start desktop**, enter your Windows account password if asked (not a
   Windows Hello PIN), and wait for **Connected**. **No host detected** does not
   block this desktop or its applications.
4. If a host is missing, use **Install a host...** or install it inside the child.
   Select **Refresh** afterward; **Browse...** finds portable/nonstandard EXEs.
5. Choose Sunshine or Apollo if several hosts are detected. Confirm **Start
   [host]**, then approve the Windows elevation prompt **inside the child preview**
   yourself. Declining startup leaves the desktop usable; Start remains available.
   VibePollo is detected but independent child startup is not supported yet.

The main streaming host can remain open. Annex creates separate child
configuration, certificates, credentials, app list and ports. It never closes or
reconfigures the console host. Concurrent encoding/capture still needs validation
on the particular host/GPU combination.

## Open the child's host settings

1. Wait for **[Host] running**. Copy the displayed **Host web UI** URL and paste it
   into a working browser on the normal desktop. **Open [host] settings** requests
   the default browser; use **Copy settings URL** if no window appears.
2. Use the URL displayed by this installation. For base port 48989 it is
   `https://127.0.0.1:48990/`. Port 47990 normally belongs to a default-port main
   host. `127.0.0.1` means this PC; do not enter it on your phone.
3. Sunshine uses a self-signed certificate. Verify the exact local URL and handle
   the browser warning yourself. Do not disable certificate validation globally.
4. Complete the host's first-use credentials/login yourself. Its web account is
   separate from your Windows password and the client's pairing PIN.

## Pair Moonlight or a compatible client

1. Put both devices on the same LAN for the first test.
2. Choose **Add PC** in the client and enter the displayed **Client address**,
   for example `PC-NAME:48989`. If the name does not resolve, substitute the host's
   active LAN IPv4 address and keep the displayed port. An example such as
   `192.168.1.50:48989` is not a fixed Annex address.
3. Select that host in the client and read its pairing PIN.
4. In the host PC's normal-desktop browser, open the **PIN** tab of the child's
   host web UI. Enter the current client PIN and a device name, then submit.
5. Return to the client and choose **Desktop**. Open games through Steam or their
   installed launchers in that streamed desktop.
6. Keep Annex attached. **Minimize to tray** frees the physical desktop.
   Verify video, audio, keyboard/mouse and controller separately before adding
   HDR, higher frame rates or different codecs. Controller drivers are installed
   separately according to the host vendor's documentation.

Apollo and VibePollo are alternative hosts, not Sunshine clients. Client forks
and host-specific extensions require their own device tests; see the
[compatibility matrix](compatibility.md).

## Open Chrome, Steam and other apps

Use **Open app** in Annex. Select an app, then **Open in child**. **Browse
app...** accepts an installed local EXE, not a shortcut, script or command string.

Chrome and Edge use persistent separate profiles automatically. Main Chrome can
stay open; do not open its normal profile in both sessions. Existing ordinary
desktop shortcuts are not rewritten. Complete first-run/sign-in choices yourself.
If the launcher exits with code 0, check the child's taskbar: an existing app may
have received the request without creating a new long-lived launcher process.

Steam for the same Windows user needs a handoff: exit Steam on the main desktop,
then open it through Annex. Annex never closes the main Steam without
your choice. Licensing, account login, external launchers and anti-cheat restrictions
remain game-specific. Wallpaper Engine can share settings between sessions; inspect
the child monitor and playback mapping before changing them.

## Update or recover the helper

Preserve `Sunshine`, `Hosts`, `cred.bin`, `display.cfg`, saved app/host selections
and logs when updating the same folder. Close this installation's viewer and idle
helper before replacing its EXE. Reopen it, then restart `Annex.exe -agent`
inside the existing child (a shortcut with `-agent` as its argument is sufficient).
Setup / repair registers the helper for future logons. Saving work and signing out
the child is another recovery option, but closes its applications.

**Helper unavailable** means transport/session/helper recovery is needed; it does
not mean Sunshine is missing. Open the registered installation's EXE. A launcher
from another folder cannot use its helper. **Status unknown** keeps Start disabled
when host identity is uncertain. **Reconnect** reattaches without signing out.
**End session...** signs out the exact child only after confirmation.

The running label verifies an identified process, not stream health. Check private
`launcher.log`, `autostart.log` and the selected child's `sunshine.log` under `Hosts`
or the preserved managed Sunshine config folder. Never publish credential, state
or private-key files. Record the first failing step and exact message before
changing ports, drivers or encoder settings.

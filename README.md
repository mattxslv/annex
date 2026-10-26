# Annex

Use a second Windows desktop while keeping the physical desktop available.
Annex attaches a native viewer to a Windows child session. Streaming is
optional: install Sunshine or Apollo, then confirm startup of a separate host
inside the child. No streaming protocol or client modifications are involved.

This is a proof of concept. A running host/app is not proof of capture, pairing,
audio, controller support or game compatibility. See [actual evidence and pending
cases](docs/compatibility.md).

## Set up

1. Put the repository (or portable ZIP) in a permanent folder owned by your
   Windows user. For an update, replace the files and preserve runtime files.
2. Open PowerShell (not as administrator) in that folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -InstallSunshine
   ```

   (Windows blocks `.\scripts\setup.ps1` directly unless scripts are allowed.) Approve
   the administrator prompts. Setup builds **Annex.exe**, enables child sessions, sets the
   RDP/compositor preferences, keeps RDP closed to the network (Remote Desktop firewall
   rules disabled; `-KeepRemoteDesktop` skips this), registers the helper as a per-user
   Startup `annex-helper.cmd`, and adds an **Annex** desktop shortcut.
   `-InstallSunshine` downloads the latest portable Sunshine from LizardByte's GitHub
   releases (checked against GitHub's SHA-256 digest) into `Sunshine\` and makes that
   folder admin-write only (see below). Leave it out if you use another host or add
   Sunshine later. `-RouteAudio` also sends Annex's own audio to the silent Steam
   Streaming Speakers so the Annex desktop's sound plays only on the client.
   **Setup / repair** in the app re-applies the Windows settings.
3. Open **Annex** and choose **Start desktop**. Enter your Windows account password if asked; a
   Windows Hello PIN is not that password. Annex checks it with Windows and saves it
   (user-scoped DPAPI, `cred.bin`) only after it signs in; the tray menu can forget it.
4. **No host detected** does not prevent using the desktop. **Install → Sunshine**
   downloads and sets up portable Sunshine for you (same as `-InstallSunshine`);
   **Install → Apollo** opens Apollo's release page. Don't use the Sunshine *installer*
   if you already run a main host such as Vibepollo or Apollo: it adds a system service
   on the same ports. Vibepollo itself can't run a second copy inside Annex.
5. After attaching, choose the host if several are found. **Browse...** handles
   portable/nonstandard locations; **Refresh** detects later installations.
   Confirm **Start [host]** to start it inside the child. Handle UAC there yourself.
   Declining leaves the desktop usable and the explicit Start action available.
6. **Open app** opens detected Chrome, Edge, Steam or Wallpaper Engine in the child.
   **Browse app...** registers another installed EXE. Browsers get a persistent
   child-only profile. Ordinary shared desktop shortcuts are not changed.
7. Follow the [PIN/pairing instructions](docs/pairing.md). Keep the viewer attached;
   **Minimize to tray** keeps it attached while freeing the main desktop.

Requirements: x64 Windows with child-session support (original proof of concept:
Windows 11 Pro), .NET Framework 4.x, built-in RDP ActiveX, a compatible encoder and
a password-capable Windows account. Domain/Entra/passwordless flows remain
unvalidated. Use a private writable install folder; runtime files live beside the
EXE. Controller drivers are a separate vendor installation, never automatic setup.

## Independent host settings

Sunshine/Apollo adapters use dedicated child configuration, state, credentials,
certificates, app list and log. New profiles live in `Hosts` beside the EXE and
choose a free complete TCP/UDP port family starting at 48989. Existing managed
`Sunshine\Sunshine\config\sunshine.conf` and pairing bytes are preserved. The main
host's credentials, configuration and preparation commands are not imported.

The main host can stay running. Annex never starts/stops its service, kills
another port listener or installs a display/controller driver. Concurrent capture
and encoding still depend on the host build/GPU. A manually launched child host
whose configuration cannot be verified stays **Status unknown**, with no guessed
pairing address. Restart it through Annex yourself to identify its settings.

VibePollo is detected, but independent child startup is explicitly unsupported in
this release. Upstream discourages multiple instances; its additional shared
helper/display state needs real coexistence verification. Select Sunshine or
Apollo for the child while leaving your main host installed.

## Apps and keyboard

Chrome/Edge opened through **Open app** use
`%LOCALAPPDATA%\Annex\Chrome-Child` / `Edge-Child`. Normal profiles, browser
processes and default associations stay intact. Complete first-run/sign-in choices
yourself. The normal browser profile cannot be opened concurrently in both sessions.

A Steam instance in another desktop produces a handoff message. Exit it there
yourself before opening it here. Account licensing, launchers, anti-cheat and RDP
restrictions remain app-specific; browsing a game EXE does not bypass them.
Wallpaper Engine can share settings: inspect its child UI and RDP monitor mapping
before changing display/playback rules. Annex does not silently change the
main wallpaper. This same-user child session cannot guarantee every app works.

Click inside the preview to give the child keyboard focus. Remote keyboard routing
is configured before every connection; unsupported properties produce a diagnostic.
Shortcut handling can differ between the embedded viewer and a streaming client;
gameplay/controller input still needs client acceptance. Click the companion
column to leave viewer focus.

## Updates and recovery

**Helper unavailable** differs from a missing streaming host. Open the EXE from
the folder registered by setup. A build-output EXE in another folder
cannot control that installation's helper. Outdated protocols require an explicit
upgrade/repair and never fall back to unvalidated commands.

Close this installation's viewer and idle helper before replacing its EXE. Reopen
it, then restart its helper inside the existing child using a shortcut to
`Annex.exe -agent`. `scripts\setup.ps1` registers that helper for future logons.
Saving work, signing out the child and reopening it is an alternative, but closes
its apps. Recovery never automatically signs out a child or closes its games.

**Reconnect** attaches again. **End session...** confirms before signing out the
exact child reported by Windows and closes its apps. Exiting only the viewer does
not sign out, but can interrupt capture. Keep `launcher.log`/`autostart.log` private.

**Resolution and scale.** By default the Annex desktop is 1920x1080 at 100%, which can look
small on a phone or tablet. Match your client by creating `display.cfg` next to Annex.exe
with one line, e.g. `2560x1600x200` for a 2560x1600 tablet at 200% scale, then Reconnect.
`display.cfg` accepts `widthxheight` or
`widthxheightxscale`, such as `2800x1272x225`. Dimensions: 200-8192; scale: 100-500
percent, or 0 for no override. Invalid input falls back atomically to 1920x1080
without a scale override. Platform/GPU limits can be stricter.

**Admin rights for the host.** Annex starts a host elevated (needed for input
into games that run as administrator) only when your normal account cannot
change the host's executable or config, e.g. Program Files installs or the
portable `Sunshine\` folder after setup locks it. Otherwise the host starts
without elevation and the status says so. Edit a locked Sunshine config through
its web UI or an elevated editor.

Setup is not transactional and records no original registry values. To uninstall,
remove this user's Startup `annex-helper.cmd`, the **Annex** desktop shortcut,
the firewall rule `Annex Sunshine` and the installation folder (the locked
`Sunshine` folder needs an elevated prompt); restore changed RDP/compositor
preferences only to your recorded previous values and only if other software
does not rely on them. Setup removes hooks left by ChildStream and earlier Annex
builds.

## Build and checks

From the repository root in Windows PowerShell 5.1 or PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

No SDK, NuGet packages, downloads or administrator rights are required. The build
uses `Framework64\v4.0.30319\csc.exe`, C# 5 and framework assemblies, treating
warnings as errors. Output: **Annex.exe**; a failed compile preserves the old
EXE. Tests exercise real local pipes/listeners and injected Windows boundaries;
they do not change real settings, sign out sessions, launch hosts/apps or log in.

CI checks both PowerShell versions and publishes the compiled EXE. Optional
`scripts\package.ps1` remains available for explicitly requested portable/source
bundles. Archive tests use temporary fixtures and remove them afterward.

`Annex.exe -preview stopped` opens a labeled synthetic native preview.
`-capture-preview <directory>` renders fixtures, not live RDP or actual OS DPI
acceptance.

[MIT license](LICENSE). Thanks to [Sunshine](https://github.com/LizardByte/Sunshine),
[Apollo](https://github.com/ClassicOldSong/Apollo),
[VibePollo](https://github.com/Nonary/Vibepollo),
[Duo](https://github.com/DuoStream/Duo) and Microsoft's
[child-session API](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions).

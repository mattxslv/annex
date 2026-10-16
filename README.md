# ChildStream

Stream a separate Windows desktop through portable [Sunshine](https://github.com/LizardByte/Sunshine) while continuing to use the physical desktop.

ChildStream creates a Windows child session through the RDP ActiveX control. Its native Windows launcher shows the desktop and Sunshine status. A small local helper runs inside that child session; when Sunshine is off, the launcher asks before starting it there. Minimizing keeps the viewer attached. No streaming protocol or client modifications are required.

This remains a proof of concept. The original repository reported high-refresh streaming; this revision does not establish a particular frame rate, GPU, Windows build, or device combination.

## Requirements

- x64 Windows 10/11 Pro with child-session support (the original proof of concept used Windows 11 Pro 25H2).
- .NET Framework 4.x and the built-in RDP ActiveX control; no NuGet packages or additional .NET SDK.
- A compatible GPU/encoder and a password-capable Windows account. The current launcher uses `MACHINE\username` and a Windows password; Windows Hello PINs are not passwords.
- A writable, private installation folder owned by the intended user. Runtime files currently live beside the executable.
- For controllers, install the appropriate virtual-gamepad driver separately using [Sunshine's Windows input instructions](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html#initial-setup). Portable setup does not install input drivers.

## Build and test

From the repository root, in Windows PowerShell 5.1 or PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

These commands need no administrator rights and do not download dependencies or change Windows settings. Build emits `ChildStream.exe` using `Framework64\v4.0.30319\csc.exe`, with compiler warnings treated as errors. Close running launchers and child helpers before replacing the executable. A compiler failure preserves the previous executable.

Packaging rebuilds and writes these files into `dist`:

- `ChildStream.exe`: unsigned Windows x64 launcher.
- `ChildStream-portable.zip`: launcher, setup scripts, source, documentation, and tests. Extract everything before setup; the executable alone is not a complete first-time installation.
- `ChildStream-source.zip`: the same source payload without the executable.
- `SHA256SUMS.txt`: checksums for the three artifacts above. Compare with `Get-FileHash -Algorithm SHA256 .\dist\ChildStream-portable.zip`.

`scripts/release-files.txt` is the source allowlist. Credentials, customized display settings, logs, installed Sunshine, Git metadata, screenshots, and scratch files are excluded. `package.ps1 -SkipBuild` packages an already built executable; use the default for a fresh release.

The suite covers configuration parsing, safe sign-out selection, credentials deletion, process/session identity, bounded pipe transport, startup confirmation policy, stale responses, native action availability, setup/build failures, archive contents, and checksums. It never calls real logoff/enable APIs, starts Sunshine, or changes firewall/registry settings. GitHub Actions runs both PowerShell versions and retains the Windows artifacts for 30 days. See [review and manual acceptance checks](docs/code-review.md) for coverage limits.

## Setup

Extract the portable ZIP into a permanent user-owned folder. Open an **administrator PowerShell as the Windows user who will run ChildStream**, change to that folder, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Setup builds the launcher and downloads portable Sunshine if absent, then enables child sessions and configures Windows RDP/compositor settings, minimized rendering, a Sunshine firewall rule, an idle helper startup hook, and a Desktop shortcut. New installs use Sunshine base port **48989**; reruns preserve existing `Sunshine\Sunshine\config\sunshine.conf` bytes. Setup changes system settings and has no automatic rollback; record previous settings if needed.

1. Open **Child Session** from the Desktop, or `ChildStream.exe`.
2. Select **Start desktop** and enter your Windows password. It is stored in user-scoped DPAPI-encrypted `cred.bin`.
3. Wait for the desktop to attach. If Sunshine is off, choose whether to start it. Approve any UAC prompt **inside the child desktop** yourself. Declining leaves it stopped; **Start Sunshine** lets you retry later.
4. When the launcher shows Sunshine running, select **Open Sunshine settings**. Configure the web UI account and pair the client using its PIN. The settings URL is local loopback on base port + 1 (normally `https://127.0.0.1:48990/`). Sunshine uses its own certificate; follow its setup documentation.
5. On your client, manually add the launcher's **Client address**, normally `<host-name>:48989`. If the name cannot resolve, use the host PC's LAN IP with that port. Discovery may find another Sunshine instance on default ports, so choose this child host explicitly.
6. Start a stream, then minimize ChildStream to the tray. Keep it running and attached while playing.

**Upgrading an existing install:** close the viewer and the old `ChildStream.exe -agent` helper before replacing files; preserve the `Sunshine` folder and your runtime configuration. Rerun setup to replace the old automatic-Sunshine startup hook. Sign out and create a fresh child desktop so the helper starts, or run `scripts\childsession-autostart.ps1` manually **inside the existing child desktop**. Do not start a second parent launcher inside the child. The GUI explains recovery if the helper is unavailable.

## Clients and host forks

ChildStream manages its dedicated portable Sunshine instance, identified by its exact executable path and child-session ID. It does not control an installed host on the physical desktop. Its protocol compatibility follows the selected Sunshine version; the following are intended client choices, not a claim that all device combinations were tested.

| Software | Role | Relationship to this build |
| --- | --- | --- |
| [Moonlight](https://moonlight-stream.org/) for PC, Android, iOS/tvOS | Client | Connect to the child Sunshine address using standard pairing/streaming. |
| [Artemis](https://github.com/ClassicOldSong/moonlight-android) | Android client | Moonlight-derived client; use the Sunshine-compatible connection mode. Host-specific extensions are outside this launcher. |
| [Moonlight TV](https://github.com/mariotaku/moonlight-tv) | webOS/embedded client | Another GameStream client; device/codec behavior needs testing on your hardware. |
| [Apollo](https://github.com/ClassicOldSong/Apollo) | Streaming host | A Sunshine fork, not a client. Replacing the managed portable host requires separate integration/testing. |
| [VibePollo](https://github.com/Nonary/Vibepollo) | Streaming host | An Apollo/Sunshine-derived host, not a client. This release does not install, detect, or manage it. |

A console Sunshine/Apollo/VibePollo installation can be separate from the child instance, provided its ports and capture/input configuration do not conflict. Starting another console host does not satisfy the child host's running check. Replacing the managed Sunshine host, adding automatic driver installation, or an installed-host management mode are larger follow-ups requiring a separate scope decision.

See the [compatibility and acceptance matrix](docs/compatibility.md) for the tests still needed on actual devices.

## Usage and recovery

- **Keep the viewer attached.** Its RDP display supplies the child desktop's capture surface. Closing the viewer can interrupt capture; games may continue in the surviving session. Reopen ChildStream and select **Start desktop** to attach again.
- **Reconnect** reconnects the viewer. Unexpected disconnections retry while the application remains open.
- **End session...** confirms before requesting sign-out of the actual Windows child session. It refuses invalid/current/console IDs and closes games. Exiting the viewer alone does not sign out.
- **Sunshine running** means the expected executable is present as a process in that child session. It is not an encoder, network, pairing, or stream-health test. Inspect Sunshine logs/settings if pairing fails.
- **Status unknown** means process identity could not be inspected safely. Start remains disabled to avoid guessing. **Helper unavailable** means the child helper needs the upgrade/recovery steps above.
- A canceled/failed elevation or immediate host exit is reported. Retry explicitly after resolving it. The launcher never starts Sunshine merely because it opens or receives a timer tick.
- Remove saved credentials from the tray's **Forget saved password** or `ChildStream.exe -resetpass`. Deletion failures are reported and the CLI returns nonzero. Domain/Entra identities and passwordless authentication remain unvalidated.
- `launcher.log` records launcher failures; `autostart.log` records helper/startup failures. Keep these private when sharing diagnostics.

### Display settings

Create `display.cfg` beside `ChildStream.exe`:

```text
2800x1272x225
```

Use `widthxheight` or `widthxheightxscale`: dimensions 200–8192 pixels each, scale 100–500 percent, or `0` for no override. The file is read on each connection. Invalid/missing settings fall back together to **1920×1080 with no scale override**. Windows/GPU capture support can impose stricter limits.

### Safe GUI preview

```powershell
.\ChildStream.exe -preview stopped
```

This labelled synthetic preview exercises native controls without reading credentials, accessing RDP sessions, or starting Sunshine. `-capture-preview <directory>` writes synthetic stopped/running/unavailable/checking, compact and enlarged-layout images. The enlarged capture is layout scaling, not an actual OS DPI transition test.

## Limitations

- One child desktop, same Windows user; platform/build support and real authentication require verification.
- No per-session HDR or custom virtual-display driver. Frame rate depends on Windows' RDP compositor, GPU and client; no performance guarantee.
- The all-users startup hook launches only an idle helper. Only a same-user launcher with verified process path and the current Windows child ID can issue supported local commands. It is not a security boundary against other programs running as that same user.
- Sunshine's elevation prompt requires human interaction in the child desktop. Helper availability and process identity can depend on Windows access restrictions.
- Local read/write operations, tray/COM events, UAC, controller/audio/capture paths, high contrast and actual multi-monitor DPI behavior are not established by automated policy tests or synthetic captures.
- Setup is not transactional, does not record original registry values, and has no automatic repair/uninstall. An existing firewall rule may need updating after moving the install folder.
- Sunshine downloads follow its latest release without version pinning/integrity verification. Portable packaging changes upstream may require setup updates.

## Uninstall

Delete the installation folder, Desktop shortcut and `childstream-sunshine.cmd` from the common Startup folder (normally `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup`). Remove firewall rule `ChildStream Sunshine`. Restore `DWMFRAMEINTERVAL`, `fDenyTSConnections` and `HKCU\Software\Microsoft\Terminal Server Client\RemoteDesktop_SuppressWhenMinimized` to their previous values as appropriate. Do not disable RDP/child sessions if other software relies on them. Setup does not record those previous values.

## Credits and license

[MIT license](LICENSE). Thanks to [Duo](https://github.com/DuoStream/Duo), [Sunshine](https://github.com/LizardByte/Sunshine), [Artemis](https://github.com/ClassicOldSong/moonlight-android), and Microsoft's documented [child sessions API](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions).

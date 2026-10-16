# ChildStream

**Stream games from a second Windows desktop while you keep using your PC — free, open, no license unlocks.**

ChildStream is a proof of concept that replicates the core idea of multiseat streaming tools (like [Duo](https://github.com/DuoStream/Duo)) using only documented Windows features:

- A **child session** — a second, fully independent Windows logon session created via the Win32 `WTSEnableChildSessions` API and the RDP ActiveX control's `ConnectToChildSession` property
- A portable **[Sunshine](https://github.com/LizardByte/Sunshine)** instance running *inside* that session on its own ports
- Any **Moonlight** client (Artemis on Android works great) connecting to it

Result: your phone/tablet/TV streams a game from the second desktop at up to ~120 fps, while the physical desktop stays fully usable at its native refresh rate (verified 143 Hz on the host while streaming 2800×1272 @ 120 fps).

```
┌────────────────────────── Your PC ──────────────────────────┐
│  Console session (you)          Child session (streaming)   │
│  ├─ your apps, 144 Hz           ├─ games                    │
│  └─ physical monitors           ├─ Sunshine (port 48989)    │
│                                 └─ RDP display @ ~120 fps   │
│  ChildStream.exe ── RDP loopback viewer ──> child session   │
└─────────────────────────────────────────────────────────────┘
                                  │
                        Moonlight / Artemis client
```

## Requirements

- x64 Windows 10/11 **Pro** (original proof of concept tested on Windows 11 Pro 25H2)
- A GPU with a hardware encoder (NVENC / AMF / QSV)
- .NET Framework 4.x (preinstalled on Windows)
- A password-capable Windows account (child session logon uses `MACHINE\user` + password; if you use a Microsoft account with Windows Hello only, allow password sign-in)
- For controller input, a compatible virtual-gamepad driver must be installed separately. Follow [Sunshine's Windows input setup](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html#initial-setup) for the version you use. ChildStream's portable download does not install drivers.

## Build and check the code

Run these from the repository root in Windows PowerShell 5.1 or PowerShell 7:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

If local script execution policy blocks these commands, run a fresh process with
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1`
(substitute `build.ps1` for a build). This does not change the saved execution policy.

Neither command requires elevation, downloads packages, or changes Windows settings.
The build uses Windows' .NET Framework 4.x compiler and framework libraries; no NuGet
packages, SDK install, or package manager are required. A failed build preserves the
previous launcher. Close ChildStream before replacing a running executable.

The test command compiles with warnings treated as errors, parses the PowerShell
scripts, and runs regression checks against display settings, sign-out selection,
password reset, setup failure handling, and Sunshine configuration preservation.
It never calls the real logoff or enable APIs. GitHub Actions runs both commands on
Windows with both PowerShell versions. The manual streaming checks are documented
in [the review report](docs/code-review.md).

## Setup

```powershell
# from an elevated PowerShell in the repo root
.\scripts\setup.ps1
```

Run setup as the Windows user who will launch ChildStream, with administrator rights.
The script:
1. Compiles `ChildStream.exe` and downloads portable Sunshine if missing, before changing Windows settings
2. Creates Sunshine's initial config on base port **48989**; reruns preserve an existing `sunshine.conf`, including encoder and port choices
3. Enables child sessions (`WTSEnableChildSessions`) and waits for the result
4. Allows RDP connections and raises the compositor rate (`DWMFRAMEINTERVAL = 8` → approximately 120 fps)
5. Keeps the viewer rendering while minimized
6. Adds an inbound firewall rule and a startup hook for non-console sessions (see the session-isolation limitation below)
7. Creates a shortcut in the user's actual Desktop folder, including redirected desktops

Then:
1. Launch **Child Session** from the desktop
2. Enter your Windows password once (stored in `cred.bin`, DPAPI-encrypted for the current Windows user)
3. Set Sunshine web UI credentials: `Sunshine\Sunshine\sunshine.exe --creds <user> <pass>`
4. Configure Sunshine's input driver if using a gamepad, then pair Moonlight/Artemis with `<host-ip>:48989` (PIN via `https://<host-ip>:48990`). Use your configured ports if you changed an existing installation.
5. Play

## Usage notes

- **Keep ChildStream running while streaming** — minimize it to the tray. The child session's display exists only while the viewer is attached; closing the app breaks capture until you reconnect (it auto-reconnects on relaunch).
- The session itself survives viewer disconnects — games keep running.
- To fully end the session, sign out from inside it or use the tray's **End session** command. The tray command targets only the child session returned by Windows and refuses the console, current session, and invalid IDs. Sign-out closes games; exiting the viewer alone does not sign out.
- If your Microsoft account password is rejected, log on as `MACHINE\username` (the launcher does this automatically).
- **Reconnect now** also starts an immediate attempt when disconnected.
- To remove the saved Windows password, use **Forget saved password** or `ChildStream.exe -resetpass`. A failed deletion is reported; the CLI returns a nonzero exit code. Use a writable, private repository folder because runtime files currently live beside the executable.

### Display settings

Create `display.cfg` beside `ChildStream.exe` to set the child desktop resolution,
with optional Windows scaling:

```text
2800x1272x225
```

Use `widthxheight` or `widthxheightxscale`. Width and height must each be 200–8192
pixels; an explicit scale must be 100–500 percent (or `0` for no override).
The file is read on each connection attempt. Missing or invalid settings fall
back together to **1920×1080 with no scale override**. These are the RDP control's
documented bounds, not a guarantee that every GPU/Windows combination can stream
every size.

## Limitations (vs. commercial tools)

- One child session max (Windows limitation), same user as the console session
- Refresh limited by the RDP compositor (~60–125 fps depending on build; `DWMFRAMEINTERVAL` tweak required)
- No per-session HDR
- A viewer connection must stay attached (commercial tools ship a custom indirect display driver to avoid this)
- **Startup isolation:** the inherited Sunshine startup hook checks only whether a session differs from the physical console. An ordinary RDP logon can also trigger it. It is not a reliable child-session detector; avoid relying on that distinction in a multi-user/RDP environment. A launcher-associated startup handshake is a proposed follow-up in the review report.
- Sunshine startup requests elevation so input can reach elevated games. Accepting its UAC prompt is still required; failures are recorded in `autostart.log`.
- Setup is not transactional and has no automatic rollback. Builds and downloads finish before system changes start, but a later failure can leave some settings changed.
- Downloads follow Sunshine's latest release and are not version-pinned. Controller behavior and portable-package compatibility can change upstream.

## Uninstall

- Delete the repo folder, the desktop shortcut, and `childstream-sunshine.cmd` from the common Startup folder (normally `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup`)
- Remove the firewall rule `ChildStream Sunshine`
- Restore `DWMFRAMEINTERVAL`, `fDenyTSConnections`, and `HKCU\Software\Microsoft\Terminal Server Client\RemoteDesktop_SuppressWhenMinimized` to their previous values as appropriate; do not disable RDP if other applications/users rely on it. Setup currently does not record previous values.
- Disable child sessions only if no other software uses them.

## Credits

- [DuoStream/Duo](https://github.com/DuoStream/Duo) for proving the concept
- [LizardByte/Sunshine](https://github.com/LizardByte/Sunshine) for the streaming host
- [Artemis / moonlight-android](https://github.com/ClassicOldSong/moonlight-android) as the client
- Microsoft's documented [Child Sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions) API

## Disclaimer

Proof of concept, provided as-is. Built collaboratively with GitHub Copilot CLI in an afternoon. Use at your own risk.

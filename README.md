# Annex

**Stream games from a second Windows desktop while you keep using your PC — free, open, no license unlocks.**

Annex is a proof of concept that replicates the core idea of multiseat streaming tools (like [Duo](https://github.com/DuoStream/Duo)) using only documented Windows features:

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
│  Annex.exe ── RDP loopback viewer ──> child session         │
└─────────────────────────────────────────────────────────────┘
                                  │
                        Moonlight / Artemis client
```

## Requirements

- Windows 10/11 **Pro** (tested on Windows 11 Pro 25H2)
- A GPU with a hardware encoder (NVENC / AMF / QSV)
- .NET Framework 4.x (preinstalled on Windows)
- A password-capable Windows account (child session logon uses `MACHINE\user` + password; if you use a Microsoft account with Windows Hello only, allow password sign-in)

## Setup

```powershell
# from an elevated PowerShell in the repo root
.\scripts\setup.ps1
```

The script:
1. Compiles `Annex.exe` from source (csc, no build tools needed)
2. Enables child sessions (`WTSEnableChildSessions`)
3. Allows RDP connections over loopback only (child sessions are loopback RDP); the Windows Remote Desktop firewall rules are disabled so RDP stays closed to the network (`-KeepRemoteDesktop` to skip)
4. Raises the session compositor rate (`DWMFRAMEINTERVAL = 8` → ~120 fps)
5. Downloads portable Sunshine and configures it on base port **48989** (coexists with an existing host like Apollo/Vibepollo on 47989); an existing config is kept on re-runs
6. Makes the Sunshine folder admin-write only, since Sunshine runs elevated (edit its config via the web UI or an elevated shell)
7. Adds a firewall rule + startup hook that auto-starts Sunshine **only inside child sessions**
8. Creates a desktop shortcut
9. Routes the viewer's audio to the silent Steam Streaming Speakers so game audio plays only on the client (uses NirSoft svcl)

Then:
1. Launch **Annex** from the desktop
2. Enter your Windows password once (stored DPAPI-encrypted, machine-local)
3. Set Sunshine web UI credentials from an elevated prompt: `Sunshine\Sunshine\sunshine.exe --creds <user> <pass>`
4. Pair Moonlight/Artemis with `<host-ip>:48989` (PIN via `https://<host-ip>:48990`)
5. Play

## Usage notes

- Machine-specific logon steps for the Annex desktop can go in an untracked `autostart.local.ps1` in the repo root; the startup hook runs it after starting Sunshine.

- **Keep Annex running while streaming** — minimize it to the tray. The child session's display exists only while the viewer is attached; closing the app breaks capture until you reconnect (it auto-reconnects on relaunch).
- The session itself survives viewer disconnects — games keep running.
- To fully end the session: sign out from inside it.
- If your Microsoft account password is rejected, log on as `MACHINE\username` (the launcher does this automatically).

## Limitations (vs. commercial tools)

- One child session max (Windows limitation), same user as the console session
- Refresh limited by the RDP compositor (~60–125 fps depending on build; `DWMFRAMEINTERVAL` tweak required)
- No per-session HDR
- A viewer connection must stay attached (commercial tools ship a custom indirect display driver to avoid this)

## Upgrading from ChildStream

The project was renamed from ChildStream. Pull, then re-run `.\scripts\setup.ps1` as admin: it builds `Annex.exe`, replaces the `Child Session` shortcut, the `ChildStream Sunshine` firewall rule and the `childstream-sunshine.cmd` startup hook, and keeps your Sunshine config and pairings. Your saved password (`cred.bin`) is kept. Delete the old `ChildStream.exe` afterwards.

## Uninstall

- Delete the repo folder (the `Sunshine` folder needs an elevated prompt), the desktop shortcut, and `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\annex-sunshine.cmd`
- Remove the firewall rule `Annex Sunshine`
- Optional: remove `DWMFRAMEINTERVAL`, set `fDenyTSConnections = 1`, and disable child sessions

## Credits

- [DuoStream/Duo](https://github.com/DuoStream/Duo) for proving the concept
- [LizardByte/Sunshine](https://github.com/LizardByte/Sunshine) for the streaming host
- [Artemis / moonlight-android](https://github.com/ClassicOldSong/moonlight-android) as the client
- Microsoft's documented [Child Sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions) API

## Disclaimer

Proof of concept, provided as-is. Built collaboratively with GitHub Copilot CLI in an afternoon. Use at your own risk.

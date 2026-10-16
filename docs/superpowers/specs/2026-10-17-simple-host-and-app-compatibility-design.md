# Simple host selection and child-session app compatibility

Status: approved by the user on 2026-10-17; implementation awaits review of the
written plan. This extends the approved Sunshine-only launcher design. It is not a
claim that these features are already in the executable.

## Outcome

Keep ChildStream a small native Windows companion: open a child desktop, optionally
stream it, and run installed applications there. The user should not need to manage
helper scripts or choose among several build folders. Deliver one compiled
ChildStream.exe for routine updates, placed in the existing installed folder.

The user's requested behavior is:

1. A missing streaming host never prevents opening the child desktop.
2. Report that no host was detected and offer Sunshine, Apollo and VibePollo install
   links. Detect an installation added while the app is open, including one installed
   from the child desktop.
3. When a host is selected and the child desktop is attached, ask to start that host
   inside that child. Declining leaves the desktop usable and exposes an explicit
   retry. Never start the console service as a substitute for the child instance.
4. Preserve independent main/child use where the host and application support it.
   Fix observed app conflicts, starting with Chrome; test Steam, games and Wallpaper
   Engine rather than equating a process with working UI or capture.
5. Keep the existing native Windows appearance and a small number of actions.

## Evidence from the current installation

- The workspace and dist executable used the current code; the Downloads install
  used an older build. The launcher in dist could not reach the helper registered
  for Downloads because peer identity and pipe names are installation-specific.
- The three known on-disk executable copies were rebuilt/synchronized, the installed
  source/scripts were refreshed, and the replacement helper was launched in the
  surviving child desktop. The updated installed viewer reached it, reported
  Sunshine stopped and displayed the confirmation prompt. No child sign-out was
  required. The idle console helper was stopped; the startup hook remains intact.
- Chrome's default user-data directory is locked by the normal desktop browser.
  Launching ordinary Chrome inside the child briefly created a process, then exited
  without a browser. A child-only user-data directory produced a persistent Chrome
  window and welcome screen while the normal desktop browser remained running.
  Authentication/first-run choices were left to the user.
- Wallpaper Engine processes exist in both sessions. This does not establish that
  its selected wallpaper, editor or monitor mapping works correctly in both.
  Its log recognizes an RDP indirect display. The observed LED-plugin load errors
  are older and do not establish the cause of this child-session complaint.
- Standard shortcut routing in the embedded window reached the physical desktop;
  the documented RDP Alt+Home shortcut opened the child Start menu. Explorer and
  mouse interaction inside the child work.
- This machine has an installed Sunshine in addition to the managed portable copy.
  Apollo and VibePollo were not found in the inspected uninstall records. Their
  actual child-session behavior has not been tested on this machine.

## Approaches considered

### Recommended: retain the Windows child session and add explicit host adapters

Keep the native viewer/helper architecture, separate desktop setup from host
availability, identify installed hosts, and give the child its own host configuration
and browser data. This meets the simple flow while preserving installed games and
the current session model. Compatibility remains dependent on the third-party app,
Windows/RDP, graphics driver and licensing.

### Only add host labels and reuse their default configuration

Less implementation work, but two host processes can contend for ports, pairing
state, logs, display changes and services. It does not meet independent main/child
use and is rejected.

### Replace the child session with a separate OS/virtual machine

Provides a stronger isolation boundary but introduces a different desktop model,
GPU provisioning, installation and game-storage requirements. This is outside the
requested child-session iteration and will not be introduced automatically.

## User flow

The existing primary Start desktop action depends only on Windows child-session
support and connection/authentication. Desktop setup remains available without a
host and does not silently download Sunshine. Where setup needs elevation, the
user handles Windows' prompt.

The Streaming host area has these states:

| State | Copy and actions |
| --- | --- |
| No host detected | "Install Sunshine, Apollo or VibePollo to stream this desktop. You can keep using the child desktop." Install links, Browse and Refresh; Start desktop remains available. |
| Host found, desktop not attached | Show detected host name; ask user to start the desktop before host startup. |
| Host found, ready | One prompt: "Start [host] in the child desktop?" Decline leaves Start [host] available. |
| Starting | Show the selected host and child-session target; suppress duplicate starts. |
| Running in child | Show child instance name, client address and web UI URL. Open/copy settings and concise pairing help remain. |
| Running only on main desktop | Report the console instance separately; it never satisfies child running status. |
| Helper unavailable or outdated | Explain the helper problem and the actual installed folder; do not mislabel this as a missing streaming host. Offer repair/restart guidance without ending the child automatically. |
| Unsupported host build or config | Show the detected software and concrete reason. Keep the desktop usable; do not claim its child instance is supported merely because an EXE was found. |
| Port/config/access/start failure | Explain the failure and offer an explicit retry. Do not stop the main host or guess that startup succeeded. |

Select a single valid discovered host automatically. With multiple installations,
use one compact chooser showing host name and location; remember the selection.
Prefer an existing valid selection, then an already running child instance, then
the existing managed portable installation. Never silently replace the user's
selection after another installer appears.

Installation links open vendor pages for the user. They do not automatically run
installers, install drivers or change the main streaming host's service settings.

## Host discovery and isolated configuration

Add a HostInstallation model and a small discovery/provider layer. Candidates come
from explicit saved selection, current managed portable location, known installation
directories, Windows uninstall records and inspectable running executable paths.
Browse handles nonstandard portable locations; do not scan entire drives.

Resolve executable/config paths before showing a usable candidate. These forks may
share sunshine.exe naming; identify branding from available file metadata and the
installation record, not the basename alone. Inaccessible candidates remain
diagnostic results rather than false "not installed" answers. Recheck paths when
an installation changes or is removed.

Each supported adapter defines its executable identity, config naming, supported
CLI invocation, process matching, port family and capture/display options. Validate
the relevant vendor version and document the tested build. Do not send Sunshine
flags blindly to an unknown fork. Keep detection separate from verified launch
capability.

The child helper launches the selected executable with a writable, dedicated child
configuration. The executable may be shared with an installed host, but ports,
state/credentials/certificates, app list and logs are separate. Do not copy the main
host's credentials or arbitrary preparation commands into the child. Preserve the
existing managed child configuration and paired devices on upgrade.

Use the existing child port when valid and free. For new profiles, choose a free
complete TCP/UDP port family starting from the existing 48989 convention. Validate
the full derived range and compare active listeners/configurations before launch;
binding can still race, so report a subsequent bind failure. Never kill another
listener. Fork-specific virtual-display/display-management options must be verified
and disabled where required for the existing RDP child capture surface. Unsupported
combinations remain explicit failures, not automatic driver changes.

Sunshine supports custom config paths and configurable state/log/app paths. This
is the basis for an independent child instance, not proof that every build or fork
supports two simultaneous streams. VibePollo's documentation discourages multiple
instances; its provider needs a real coexistence test before marking that scenario
verified. The UI must distinguish detected, child-running and stream-tested.

## Helper and executable updates

Keep bounded local IPC, same-user/network ACL restrictions, actual child-session
validation and native peer executable checks. Extend the protocol with an explicit
version handshake and bounded structured host-selection/status data. Never accept
arbitrary shell command text from a pipe request.

The executable must contain the functionality/resources required for its helper and
desktop setup. Change the startup hook to call the installed EXE's -agent mode
directly so routine EXE updates do not depend on replacing a PowerShell script.
External streaming-host binaries remain separately installed dependencies.

Record the registered installation path, show precise wrong-folder/outdated-helper
diagnostics, and provide a clear update/recovery path. No silent child sign-out.
During an update, close/restart only launcher/helper processes that belong to that
installation. If a helper cannot be restarted without ending applications, ask the
user after saving work; do not terminate the session automatically. Future delivery
contains only the EXE unless the user asks for additional artifacts.

## App compatibility

This remains a same-user Windows child session, not an isolated PC. A host becoming
running cannot change an application's global mutex, shared files, licensing,
anti-cheat or RDP restrictions. Universal "every app works" is not an acceptance
claim this architecture can honestly satisfy.

Prepare a small child app launcher reachable from the companion, executing in the
helper's actual child session. Include detected Chrome, Edge, Steam and Wallpaper
Engine, plus a file-picker fallback for an installed app or game. Use structured
executable/argument records and normal Windows launch semantics, not shell command
strings. Validate the child connection again at the launch boundary and report
process exit/errors; a new process alone is not success. Authentication stays with
the user.

For Chrome/Edge, use a persistent child-only user-data directory. Keep the main
desktop profile and default browser associations intact. Reuse the child profile
on reconnect; never copy a live normal profile, remove lock files, disable the
browser sandbox, or kill the main browser. Expose the child browser action without
requiring manual flags. Changes to ordinary shared desktop shortcuts are avoided
because the same Windows user's Desktop is visible in both sessions.

For Steam, launch the actual installed client inside the child and test a selected
installed game. If another-session Steam instance or the account prevents this,
report it and offer an explicit user-directed handoff. Do not close the main Steam
session automatically or promise simultaneous use of the same Steam account.
Test a game launch and interaction separately from opening Steam. Do not purchase,
download a new game, bypass anti-cheat or change account authentication to pass.

For Wallpaper Engine, inspect the child UI and active RDP monitor mapping, compare
its selected display and playback rules, and test an existing wallpaper. A process
in both sessions does not prove a rendering result. Shared configuration changes
that affect the normal desktop require an explicit choice; do not silently replace
the user's wallpapers or global settings. Report version/driver limitations if no
supported per-session configuration can be established.

Set/document remote keyboard routing while the embedded viewer is focused, retain
a clear way to return to the physical desktop, and verify taskbar/Start, text input,
mouse and window resizing. Unsupported COM settings get visible/logged diagnostics
rather than being silently ignored when they affect usability.

## Validation and completion

Automated tests cover: missing host plus usable desktop; installation discovery,
multiple/removed/inaccessible candidates; branding ambiguity; delayed detection;
selected-host prompts and declines; console versus child identity; complete port
families/conflicts; separate profile/state paths; invalid configuration; duplicate
starts; UAC cancellation; early exit; stale replies/selections; helper version/path
mismatch; safe child-only app launch records and browser profile separation.

Keep the existing non-destructive suite and both PowerShell versions. Compile x64
with warnings as errors. Review normal/compact host states and app-picker UI in a
bounded native visual pass, then obtain independent code/UI review. Do not add
browser/web tooling or packages to this WinForms app just for a design skill.

Manual acceptance distinguishes verified from pending:

- Start/reconnect the child with all supported hosts absent.
- Install/select one host after attachment; approve/decline and retry its launch.
- Verify executable, child session, separate config and full listener family.
- Pair a real client and test video/audio/input while the main desktop stays usable.
- Test main/child host coexistence per provider; document exact builds and failures.
- Keep normal Chrome open while child Chrome opens and navigates with its own data.
- Launch Steam and an existing user-selected game in the child; test input/rendering.
- Verify an existing Wallpaper Engine wallpaper in the RDP child and normal desktop.
- Exercise focus/shortcut routing, minimize/reconnect and helper update recovery.

Ship only behavior actually implemented and verified. Pending fork, device, game,
driver or account cases remain explicit. Keep setup/download/driver/security changes
separate from ordinary startup. The existing helper ACL and native target safeguards
must remain intact.

## Primary references

- [Windows child sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions)
- [RDP keyboard routing](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientsecuredsettings-keyboardhookmode)
- [RDP alternate shortcuts](https://learn.microsoft.com/en-us/windows/win32/termserv/terminal-services-shortcut-keys)
- [Sunshine configuration](https://github.com/LizardByte/Sunshine/blob/master/docs/configuration.md)
- [Apollo getting started](https://github.com/ClassicOldSong/Apollo/blob/master/docs/getting_started.md)
- [VibePollo getting started](https://github.com/Nonary/Vibepollo/blob/master/docs/getting_started.md)
- [VibePollo configuration](https://github.com/Nonary/Vibepollo/blob/master/docs/configuration.md)
- [Chromium independent profiles](https://www.chromium.org/developers/creating-and-using-profiles)
- [Chromium Windows profile singleton/lock](https://raw.githubusercontent.com/chromium/chromium/main/chrome/browser/process_singleton_win.cc)
- [Steam account use](https://help.steampowered.com/en/faqs/view/71EA-CDCE-FB5C-82B3)
- [Wallpaper Engine monitor mapping](https://help.wallpaperengine.io/functionality/automaticstartup.html)

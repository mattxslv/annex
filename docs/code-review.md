# ChildStream code review and improvements

Review date: 2026-10-16. Baseline: `d41693eb27691516e96df42a05396f1dfe3bd17c`
from [mattxslv/childstream](https://github.com/mattxslv/childstream).

The review covered all launcher code, both original PowerShell scripts, the README,
ignore rules, and relevant history. The goal is dependable second-desktop game
streaming while the physical desktop remains usable. This change keeps the current
.NET Framework/WinForms and portable Sunshine architecture, without package
dependencies. The user subsequently approved the native launcher and protected
child-session helper in [the design](superpowers/specs/2026-10-16-streaming-launcher-design.md).

## Findings addressed

| Priority | Original behavior and consequence | Change |
| --- | --- | --- |
| High | End session parses localized `qwinsta` text, matches username substrings, and logs off every matching non-console row. It can select unrelated sessions. | Query `WTSGetChildSessionId` for this parent and call `WTSLogoffSession` only for that ID. Refuse missing/sentinel, zero, current, and console IDs. Report errors and keep the viewer open on failure. |
| High | Setup ignores compiler failure and can run a stale launcher before changing Windows settings. Direct GUI-executable invocation also does not reliably await `-enable`. | Separate non-admin build, explicitly check the compiler result, publish output only on success, and use `Start-Process -Wait -PassThru` for enablement. Check native query success in the launcher. |
| Medium | Every setup rerun replaces `sunshine.conf`, discarding user settings. | Create initial UTF-8 config only when absent; preserve existing bytes. |
| Medium | Partial parsing of `display.cfg` can retain a new width but an old height, or pass invalid sizes into COM and prevent connection. The reused COM control can also retain an earlier scaling override. | Validate the entire configuration within documented RDP limits before using it. Invalid settings fall back together, restoring original scale properties when an override is removed. |
| Medium | Password deletion exceptions are swallowed while the UI/CLI report success. | Share deletion handling, show a failure message, and return nonzero for CLI failure. |
| Medium | Setup assumes a literal Desktop and common Startup path. Redirected folders can break installation. CMD interpolation also mishandles some checkout paths. | Resolve Windows known folders and encode the startup PowerShell command so spaces, apostrophes, non-ASCII characters, and CMD metacharacters in the script path are preserved. |
| Medium | Failed Sunshine elevation is not usefully recorded by the hidden startup hook. | Stop on startup errors, record failure, and propagate it. |
| Low | Reconnect now does nothing while disconnected. | Immediately attempt connection in that state; retain existing reconnect behavior after an explicit disconnect. |
| Medium | No repeatable build-only command, automated regression suite, or CI exists. | Add build/test commands and Windows checks for PowerShell 5.1 and 7. Tests avoid host settings and real session termination. |
| Medium | Pair-and-play instructions omit the virtual-gamepad driver prerequisite. | Link the current Sunshine input instructions and state that the portable setup does not install a driver. |

Downloads and folder checks now finish before setup enables sessions or changes
registry/firewall settings. A missing expected Sunshine release asset or executable
produces a clear failure. This reduces partial setup; it is not a full rollback
implementation.

The existing DPAPI user scope, loopback server destination, MIT license, port
separation, and destructive sign-out confirmation are retained. The repository
already had an appropriate `.gitignore` for credentials and runtime output.

## Approved launcher follow-up implemented

The inherited non-console automatic-Sunshine hook was replaced with an idle helper.
The parent queries its actual Windows child ID; every local request verifies the
same-user pipe ACL, peer executable path, peer session and current child association.
Only bounded STATUS/START messages are accepted. Process detection matches the
portable executable path and child ID; uncertain access suppresses starting.
Serialized host startup runs on an STA thread in the child, so UAC stays there.

The native launcher adds setup/session/host states, confirmation before host start,
pairing address, loopback settings, tray handling and safe sign-out. It discards
stale asynchronous responses after session changes, disconnect or close, and prompts
once per observed host stop. A new child first shows Checking Sunshine; routine
checks preserve the prior snapshot. Per-session/user mutexes guard duplicate
launchers/helpers. The helper never starts a host solely because Windows logs on.

The independent whole-branch review found two confirmation lifecycle issues:
timer/reconnect events can change the child while a modal question remains open.
Both start and sign-out approvals now capture the child and connection generation,
and expire after disconnect, replacement or close. Sign-out additionally compares
the fresh native child lookup with the approved target before calling logoff.
Regressions cover reused IDs, replacement/closed connections and that native boundary.

The native finish review accepted the scored checking-state correction after fresh
stopped/running/unavailable/compact/enlarged/checking captures. These are labelled
synthetic previews, not real session, capture or OS DPI evidence. Native Tab focus,
Return on Reconnect and Space on simulated Start Sunshine were exercised safely.
The pairing follow-up disables Copy address when no address is known, shows the
validated web UI URL with a manual-copy fallback, and adds inline PIN-pairing steps.
Both host and client destinations are explicit, so a browser inside the child is
not required. A regression also keeps expanded instructions readable after resizing.

During the follow-up, a real child desktop was observed connected with Steam
rendering and mouse input working. Its managed Sunshine process listened on the
configured stream and web UI ports, separately from a console host on default
ports. Sunshine detected H.264/HEVC NVENC encoders. The web UI was accepting TCP
connections, but certificate review and first-time credentials required human
interaction. The normal URL handler did not visibly open a browser in the test;
this motivated exposing the manual URL and recovery text. Real client pairing and
audio/video/input streaming were not certified by those observations. Chrome was
observed in the parent session; a shared-profile conflict in the child remains a
possible cause, not a confirmed diagnosis. See [the pairing guide](pairing.md).

Release packaging uses an explicit source-file allowlist. ZIP-content tests seed
harmless credential/log/host/Git/scratch stand-ins and verify they never enter the
archives. Checksums cover the executable and both ZIPs. Sunshine and drivers are
not redistributed. Client/host roles are documented in the README; Apollo and
VibePollo are alternative hosts, not clients of Sunshine.

## Remaining findings and proposed major follow-ups

These are suggestions requiring a separate scope decision, not implemented features.

1. **Setup recovery and uninstall.** Record original registry values and owned
   firewall/startup artifacts; add repair and uninstall that restore only owned
   changes. Existing firewall rules also retain an old executable path if the
   checkout is moved. Avoid blindly restoring defaults that could break existing RDP.
2. **Deeper preflight and diagnostics.** The status screen checks session/helper and
   host process state. Additional checks could explain Windows edition,
   session support, encoder availability, port conflicts, input drivers, UAC failures,
   and Sunshine startup health before attempting a stream. Combine this with pinned,
   integrity-checked Sunshine downloads for reproducible installs.
3. **Authentication and storage.** The launcher still uses the existing
   `MACHINE\username` password flow and stores runtime state beside the executable.
   Domain/Entra identities, passwordless parent sessions,
   and per-user application-data storage need explicit design and compatibility
   checks. Microsoft documents automatic authentication for normal child sessions,
   so mandatory password collection may be removable after validation.

A custom virtual display driver or HDR support would be much larger projects and
are not assumed necessary for the current proof of concept.

## Verification and its limits

The baseline compiled with the installed .NET Framework compiler. The changed
launcher builds with warnings treated as errors. `scripts/test.ps1` parses
PowerShell and runs C# policy/native-boundary checks, PowerShell script regression
checks, and packaging ZIP/checksum checks. Run the command for current case totals.
Both Windows PowerShell 5.1 and PowerShell 7 are covered by local verification and
the Windows CI matrix. Hosted CI results must be checked on the final commit;
a local run alone does not establish them.

Tests exercise real display parsing, file deletion and config/build filesystem
behavior. Session API/process boundaries are substituted so tests cannot log off
a session, enable child sessions, elevate Sunshine, or change firewall/registry
settings. Baseline expectations were run failing before the main fixes; additional
password-reset checks cover an actual read-only file. Transport checks also exercise
real local named pipes, peer PIDs/session/path inspection, ACLs and timeouts in a
normal user process. Development app-container isolation prevents those peers
communicating, so that suite runs outside the development sandbox; it needs no
administrator rights or host-setting changes.

The automated suite does not establish real RDP COM event behavior, gamepad input,
UAC interaction, capture performance, authentication, or end-to-end streaming.
Setup was not run against the development PC's system settings.

### Manual acceptance check on a dedicated test PC

1. Record existing RDP/registry/firewall values, then run elevated setup from a
   folder writable by the intended user. Repeat setup after customizing Sunshine's
   encoder/ports; verify those settings survive.
2. Launch the viewer, select Start desktop, authenticate, choose to start Sunshine,
   and accept its elevation inside the child desktop. Verify a child
   desktop and a Sunshine process in that desktop; confirm the physical desktop
   remains usable. Pair a Moonlight client and test video, audio, keyboard, mouse,
   and a controller with its required driver installed.
3. Minimize/restore the viewer and verify capture continues. Disconnect and use
   Reconnect; verify the game survives. Change the display file and reconnect.
   Test actual 100%, 150% and 200% display scaling, high contrast, keyboard navigation
   and screen-reader announcements. Synthetic enlarged-layout captures do not replace these.
4. Decline the initial start question and verify it does not recur on every timer.
   Start explicitly, reject/cancel elevation and verify the launcher/log report
   failure with an explicit retry. Stop a running host and check the new stop prompt.
   Verify the settings URL and client address reflect a non-default configured port.
   Verify a
   malformed display file still permits a connection with the default resolution.
5. Test password forgetting, then **End session** with a disposable game/application.
   Verify only the actual child signs out, the viewer exits, and it does not recreate
   the session. Keep unrelated console/RDP sessions open to check they survive.
6. Keep an unrelated console host and an ordinary RDP session open. Confirm their
   hosts do not satisfy the child's running check and a new RDP logon launches only
   an idle helper. Upgrade an existing child without a helper and verify recovery
   guidance. Test helper crashes, permission-denied process inspection and child
   changes during a delayed response. Verify only the current child can receive START.
7. Test Moonlight PC, Android and iOS/tvOS, Artemis, and Moonlight TV on actual
   target devices with supported codecs. Record versions, pairing, video/audio,
   keyboard/mouse/controller and reconnect results. Host forks need a separate
   replacement integration; they are not client acceptance cases.

## References

- [Microsoft child sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions)
- [WTSGetChildSessionId](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsgetchildsessionid)
- [WTSLogoffSession and asynchronous-result semantics](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtslogoffsession)
- [RDP desktop width](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscax-desktopwidth) and [height](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscax-desktopheight)
- [RDP scale-factor properties](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property)
- [Windows special-folder resolution](https://learn.microsoft.com/en-us/dotnet/api/system.environment.getfolderpath)
- [Sunshine setup and Windows input drivers](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html)

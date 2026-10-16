# ChildStream code review and improvements

Review date: 2026-10-16. Baseline: `d41693eb27691516e96df42a05396f1dfe3bd17c`
from [mattxslv/childstream](https://github.com/mattxslv/childstream).

The review covered all launcher code, both original PowerShell scripts, the README,
ignore rules, and relevant history. The goal is dependable second-desktop game
streaming while the physical desktop remains usable. This change keeps the current
.NET Framework/WinForms and portable Sunshine architecture. It adds no package
dependencies or major product features.

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

## Remaining findings and proposed major follow-ups

These are suggestions requiring a separate scope decision, not implemented features.

1. **Restrict automatic Sunshine startup to the launcher's child session.**
   The current hook treats any non-console session as a child. Ordinary RDP logons
   and another user's logon can trigger it, and its process-name check does not
   distinguish Sunshine installations. Replace the all-users heuristic with an
   authenticated association between the launcher and its child session, including
   reconnect and viewer-exit behavior. This should be prioritized before multi-user
   use. `WTSGetChildSessionId` documents a query for the *current session's child*;
   simply calling it inside a child is not a documented way to identify its parent.
2. **Setup recovery and uninstall.** Record original registry values and owned
   firewall/startup artifacts; add repair and uninstall that restore only owned
   changes. Existing firewall rules also retain an old executable path if the
   checkout is moved. Avoid blindly restoring defaults that could break existing RDP.
3. **Preflight and diagnostics.** A small status screen could explain Windows edition,
   session support, encoder availability, port conflicts, input drivers, UAC failures,
   and Sunshine startup health before attempting a stream. Combine this with pinned,
   integrity-checked Sunshine downloads for reproducible installs.
4. **Authentication and instance ownership.** The launcher still uses the existing
   `MACHINE\username` password flow and stores runtime state beside the executable.
   Domain/Entra identities, passwordless parent sessions, single-instance ownership,
   and per-user application-data storage need explicit design and compatibility
   checks. Microsoft documents automatic authentication for normal child sessions,
   so mandatory password collection may be removable after validation.

A custom virtual display driver or HDR support would be much larger projects and
are not assumed necessary for the current proof of concept.

## Verification and its limits

The baseline compiled with the installed .NET Framework compiler. The changed
launcher builds with warnings treated as errors. `scripts/test.ps1` runs 28 C# and
5 PowerShell regression cases, plus PowerShell parsing. The suite was run in both
Windows PowerShell 5.1 and PowerShell 7. Git diff whitespace checks were also run.
The CI workflow is provided; its hosted result is not established by a local run.

Tests exercise real display parsing, file deletion and config/build filesystem
behavior. Session API/process boundaries are substituted so tests cannot log off
a session, enable child sessions, elevate Sunshine, or change firewall/registry
settings. Baseline expectations were run failing before the main fixes; additional
password-reset checks cover an actual read-only file.

The automated suite does not establish real RDP COM event behavior, gamepad input,
UAC interaction, capture performance, authentication, or end-to-end streaming.
Setup was not run against the development PC's system settings.

### Manual acceptance check on a dedicated test PC

1. Record existing RDP/registry/firewall values, then run elevated setup from a
   folder writable by the intended user. Repeat setup after customizing Sunshine's
   encoder/ports; verify those settings survive.
2. Launch the viewer, authenticate, and accept Sunshine elevation. Verify a child
   desktop and a Sunshine process in that desktop; confirm the physical desktop
   remains usable. Pair a Moonlight client and test video, audio, keyboard, mouse,
   and a controller with its required driver installed.
3. Minimize/restore the viewer and verify capture continues. Disconnect and use
   Reconnect now; verify the game survives. Change the display file and reconnect.
4. Reject/cancel elevation and verify the startup log reports failure. Verify a
   malformed display file still permits a connection with the default resolution.
5. Test password forgetting, then **End session** with a disposable game/application.
   Verify only the actual child signs out, the viewer exits, and it does not recreate
   the session. Keep unrelated console/RDP sessions open to check they survive.
6. Treat ordinary RDP logon startup as a known limitation until the association
   follow-up is implemented. Do not assume the hook isolates users.

## References

- [Microsoft child sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions)
- [WTSGetChildSessionId](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsgetchildsessionid)
- [WTSLogoffSession and asynchronous-result semantics](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtslogoffsession)
- [RDP desktop width](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscax-desktopwidth) and [height](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscax-desktopheight)
- [RDP scale-factor properties](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property)
- [Windows special-folder resolution](https://learn.microsoft.com/en-us/dotnet/api/system.environment.getfolderpath)
- [Sunshine setup and Windows input drivers](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html)

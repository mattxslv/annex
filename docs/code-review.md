# ChildStream review and validation

Baseline: `d41693eb27691516e96df42a05396f1dfe3bd17c` from
[mattxslv/childstream](https://github.com/mattxslv/childstream).
Review and implementation: 2026-10-16–17, `review/reliability`.
The approved design and plan are in `docs/superpowers`.

## Addressed defects

| Area | Defect/consequence | Implemented behavior |
| --- | --- | --- |
| Session termination | Localized text parsing could sign out unrelated sessions | Native parent child lookup; reject service/console/self/sentinels; revalidate the approved child and connection generation before logoff |
| Build/setup | Failed compilation could leave setup running a stale EXE | Separate warnings-as-errors build, temporary output and explicit exit checks; EXE-contained setup with injected native boundaries |
| Display/credentials | Partial display parsing and swallowed deletion errors | Atomic validation/fallback, restored COM scale defaults and truthful credential deletion results |
| Installation | Scripts/Startup paths and mandatory host download complicated setup | Direct per-user EXE helper shortcut and installation record; optional hosts; thin developer setup wrapper |
| Host identity | Main host or unknown child configuration could be mistaken for managed host | Exact executable/session/config identity, owned PID/start-time/profile receipts, unknown state when unverifiable |
| Host separation | Shared state/ports could conflict with the main host | Dedicated child config/state/credentials/certificates/apps/log; complete IPv4/IPv6 TCP/UDP port-family preflight; main configuration untouched |
| Helper transport | Unscoped/stale requests and command text are unsafe | Protocol 2 typed bounded frames, same-user ACL/network deny, native peer executable/session checks, helper-instance selection IDs and current-parent child lookup before each request |
| Apps | Chrome shortcuts could reuse the active main profile | Persistent Chrome/Edge child profiles through Open app; registered local EXEs only; native argument quoting |
| Steam | Starting another session could redirect to the main instance | Explicit conflict/handoff result; no automatic process termination |
| Confirmation | Modal approvals could outlive the child, selection or edited config | Capture connection/selection/profile identity; expire stale approvals; retain pending launch ownership across config refresh |
| Discovery | Failed inspection could look like no installed host; polling erased preconnect results | Failed/ambiguous scans remain Unknown; stable disconnected state preserves discovery |
| Live child guard | Helper queried WTSGetChildSessionId inside the child and demanded a child of its own | Parent retains actual child lookup before every request; helper validates its own bound non-console session and native peers |
| Live folder comparison | Correct installation warned about another folder because of a final separator | Dedicated normalized installation-root comparison, including case/trailing separator regression |
| UI/recovery | Browser handler failure made pairing opaque | Visible validated Host web UI URL/copy fallback, client address/PIN steps, explicit missing/helper/unknown states, always-available Setup / repair |

The child guard fix follows Microsoft's API contract: WTSGetChildSessionId queries
a child of the **calling** session. It belongs in the parent request path. A child
helper cannot use it to identify itself. Native peer and parent association checks
were retained; no session boundary was replaced with a username/text match.

## Independent review

The independent whole-branch code review at `4abb081` found no Critical findings
and four Important findings: edited-profile approval reuse, pending startup
replacement, discovery overwrite while disconnected and false host absence.
Meaningful regressions reproduced all four (142/146), then passed after fixes
(148 C# cases). The fixes are committed in `8b27721`.

The independent native finish review used explicitly synthetic fixture captures.
Its one scored feedback issue, Sunshine-specific copy after selecting Apollo, was
fixed with generic Host web UI feedback. Confirmation disposition: **ship for that
scored fix and fixture scope**. It does not certify live RDP, OS DPI, streaming or
accessibility. Two bounded author fixture passes were completed; no further visual
polish loop was performed. PRODUCT.md/DESIGN.md and the Impeccable sidecar retain
the native design system.

The subsequent live helper/folder defects each have RED/GREEN regressions, bringing
the suite to 150 C# cases. Real child Chrome navigation, Steam handoff, game
rendering and separate Sunshine listeners are documented in
[the compatibility matrix](compatibility.md).

## Verification

The current suite contains **150 C# + 7 PowerShell + 3 packaging cases**.
Windows PowerShell 5.1 and PowerShell 7 run the same tests. Production build uses
the installed x64 .NET Framework compiler and treats warnings as errors.
Whitespace validation uses `git diff --check`. Hosted Windows CI must be checked
against the final pushed SHA; local tests alone do not establish hosted success.

Tests exercise configuration/display parsing, file operations, native argument
round trips, launch/confirmation policies, stale identities, actual local pipes,
peer identities/ACLs/timeouts and IPv4/IPv6 listener inventory. Injected system
boundaries prevent them from changing real Windows settings, logging off sessions,
elevating hosts, launching apps or handling credentials. The local pipe suite runs
outside app-container isolation, requiring no administrator rights.

Temporary package fixtures verify explicit source allowlists and exclusion of
credentials, host state, logs, Git and scratch data. Optional package tooling is
retained, but routine CI/user delivery is the compiled EXE only.

## Actual limitations and next acceptance

- A running process and listening port do not establish capture, pairing, audio,
  controller support, stable concurrent encoders or end-to-end streaming.
- Sunshine 2026.914.233613 was started alongside the main instance on this PC;
  Apollo real startup is pending. VibePollo discovery is supported, but independent
  child startup is explicitly refused until its shared helper/display state and
  coexistence contract are verified.
- Same-user child desktops share third-party state. Steam requires handoff; DRM,
  anti-cheat, account flows, Wallpaper Engine monitor mapping and shared settings
  cannot be solved by claiming universal app compatibility.
- No fresh Windows setup was run solely for verification. Setup is not
  transactional and records no original registry values; repair/uninstall cannot
  safely restore unknown previous values.
- Windows password collection, domain/Entra/passwordless flows, real multi-monitor
  DPI/high contrast/screen-reader behavior and client-specific input remain
  additional acceptance work.
- UAC, authentication and certificate warnings require human handling. No automatic
  security-setting changes, driver installs or safety-dialog bypasses were used.

The existing DPAPI user scope, loopback RDP destination, MIT license, separate
ports and sign-out confirmation remain. This is not a security boundary against
other programs running as the same Windows user.

## Retained decisions

- **Retain VibePollo start refusal:** shared helper/display/coexistence behavior
  lacks hardware evidence. Cost: a potentially working build still requires
  Sunshine/Apollo for child startup until a separate integration is verified.
- **Persist owned launch receipts:** Windows can hide elevated host command lines.
  PID/start time/exact path/session/profile digest support recovery without
  guessing a manually launched host's ports.
- **Use helper-instance scoped selection IDs:** old helper/replaced executable or
  edited profile approvals expire while the 4096-byte transport bound stays fixed.
- **Keep actual child lookup in the parent and bound-session checks in helper:**
  this matches the calling-session API contract while retaining native peer checks.
- **Keep live and synthetic results separate:** a pending device/app combination
  remains pending even when policy tests and fixture layouts pass.

## References

- [Microsoft child sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions)
- [WTSGetChildSessionId](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsgetchildsessionid)
- [WTSLogoffSession](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtslogoffsession)
- [RDP width](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscax-desktopwidth) and [height](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscax-desktopheight)
- [Chromium independent profiles](https://www.chromium.org/developers/creating-and-using-profiles)
- [Wallpaper Engine monitor controls](https://help.wallpaperengine.io/en/functionality/cli.html)

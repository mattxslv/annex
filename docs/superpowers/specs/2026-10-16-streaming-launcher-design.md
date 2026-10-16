# Streaming launcher design

## Approved purpose

The user selected the existing child-desktop flow: ChildStream manages a separate
Windows child session and a portable Sunshine instance inside it. The console
desktop remains usable. Add a sleek, simple native GUI, show host status, ask
before starting a stopped host, compile a Windows executable, and publish the
changes upstream where permissions allow.

## Scope and constraints

- Keep .NET Framework, x64 WinForms, RDP ActiveX, and the existing build scripts.
- Preserve tray/reconnect, display settings, credentials, and safe child sign-out.
- Do not count Sunshine running in a different Windows session as the child's host.
- Keep Sunshine's standard streaming protocol; Moonlight/compatible clients pair
  with the child instance. Apollo and VibePollo are host products, not clients.
- Do not claim fork-specific features or real-device compatibility without tests.
- No automatic driver installation, changes to an installed host/service, custom
  display driver, HDR project, or full rollback installer in this scope.

## User flow

1. Open a compact native window with ChildStream identity, session status, streaming
   host status, pairing address/port, and one clear action for the current state.
2. Start/reconnect the child desktop using the existing viewer. Keep credentials in
   the current flow; missing session prerequisites show actionable instructions.
3. Once the actual child ID is known, query that session's host helper. Show
   checking, running, stopped, missing, unavailable, and failure states distinctly.
4. If Sunshine is stopped, say so and ask whether to start it. Declining leaves it
   stopped and offers a Start Sunshine action; never prompt on every timer tick.
5. Starting happens inside the child session, with Windows UAC there if needed.
   A launched process is not evidence of a healthy encoder or successful stream.
6. Show the viewer, minimize to tray, open Sunshine settings, retry, and sign out
   from visible controls. Sign-out retains its destructive confirmation.

## Session-local startup helper

The old all-users startup hook starts Sunshine for any non-console logon. Replace
that behavior with a lightweight `ChildStream.exe -agent` helper at logon. Helpers
do not start a host on their own. The launcher requests status/start from the helper
whose Windows session ID matches `WTSGetChildSessionId`.

Use local named pipes protected by the current Windows user's SID, with session IDs
in their names. Restrict requests to a small status/start protocol; the request must
not carry an executable path or shell command. The helper launches only the app's
configured portable Sunshine executable with its explicit child configuration.
Validate the pipe server's session on the client and the caller's application
identity on the server before accepting a start request. Reject malformed/oversize
messages, unavailable peers, mismatched sessions, and timeouts. Serialize start
requests and use a per-session helper mutex to prevent duplicate helpers.

The first setup must register the helper; existing child sessions may require sign
out/in after upgrading. If no helper is reachable, show that recovery rather than
starting Sunshine on the console. Helpers in ordinary RDP/console sessions remain
idle unless addressed as the actual child by the launcher.

## Host state and configuration

Detect Sunshine by expected executable path and session, not process name alone.
Treat denied process inspection as unknown instead of stopped, so it cannot cause
duplicate launches. Recheck before starting to handle repeated clicks/races. Report
missing executable/config, denied elevation, early process exit, and startup errors.

Read the base port from the managed Sunshine config, defaulting to 48989 for a new
install. Validate the port range and prevent malformed configuration from generating
an incorrect pairing address or settings URL. Open only the validated loopback web
UI address. Display running status without claiming pairing or streaming health.

## GUI direction

An Operate surface extending the native viewer: a quiet pale slate background,
strong dark text, restrained blue primary actions, meaningful status colors with
text labels, generous spacing, and native window chrome. Use Segoe UI for Windows
task clarity. Keep the desktop preview as the main workspace after connecting;
put status and actions in a compact companion panel rather than decorative cards.
Support keyboard navigation, high contrast, DPI scaling, resizing, and readable
error states. A preview/capture mode must not enable sessions or launch a host.

## Verification and delivery

- Retain and run the existing regression suite under PowerShell 5.1 and 7.
- Add meaningful tests for session/path matching, access denial, stopped/running/
  missing states, start races, cancel/failure, config ports, protocol bounds,
  pipe authorization/session identity, timeouts, and UI state decisions.
- Test real local pipe transport without calling real logoff/enable APIs or
  elevating a host; use isolated process/filesystem boundaries for side effects.
- Build and visually inspect native GUI captures at normal and larger DPI/layout
  sizes. Exercise the GUI without changing host settings through a preview mode.
- Independently review the code and UI, then fix material findings and rerun checks.
- Deliver an executable and portable/source bundles with exact build instructions.
  State unsigned status and any hardware/client smoke tests not actually performed.
- Push to the authenticated user's fork and create/update an upstream pull request.
  Upstream merge requires maintainer permission; no write permission is available
  for the original repository under the current account.

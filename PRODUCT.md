# Product

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop, native WinForms. This platform is outside the skill's web/iOS/Android taxonomy; retain the actual Windows stack and native conventions.

## Users and purpose

PC users who want to use games or applications in a second Windows desktop while
continuing to use the physical desktop. ChildStream keeps a native RDP viewer
attached to a same-user Windows child session. Streaming is optional; a supported
installed host can run a separate child instance after explicit confirmation.

## Operating context

x64 Windows with child-session support, .NET Framework 4.x and built-in RDP ActiveX.
Streaming also needs a compatible encoder and Moonlight-family client. Sunshine,
Apollo and VibePollo are hosts, not clients. Discovery is separate from verified
startup, coexistence and streaming: Sunshine/Apollo have child adapters; VibePollo
is detected but child startup remains unsupported pending coexistence verification.
This is a same-user session, not an isolated PC; app licensing, shared settings,
global mutexes, anti-cheat and RDP restrictions still apply.

## Confirmed capabilities and constraints

- Native GUI and tray, desktop/reconnect controls and session-scoped host status.
- Setup / repair is always visible in the footer. Setup and the idle helper are
  contained in the EXE; elevated setup enables the desktop and registers that
  installation's EXE helper. It does not download a streaming host.
- A missing host does not block the desktop. Host selection, Refresh, Browse and
  vendor install links support installed and later-added hosts. Startup names the
  selected host and asks before starting it inside the attached child; no console
  host or service is used as a fallback.
- Supported child hosts have their own profiles, complete port families, pairing
  state, credentials, app lists and logs. Preserve existing managed configuration
  and paired devices. Do not import the main host's settings or stop its listeners.
- Open app provides a native child-session picker for detected or registered EXEs.
  Chrome/Edge use persistent child-only data. Steam conflicts require an explicit
  user handoff; Wallpaper Engine may share settings and needs child display checks.
  Dispatch feedback never certifies visible UI, rendering, sign-in or a working game.
- Remote keyboard routing is applied before each RDP connection; Alt+Home reaches
  the child Start menu and clicking the companion column leaves viewer focus.
  Unsupported routing produces a diagnostic rather than a silent success claim.
- Deliver the compiled ChildStream.exe by default, keeping known application copies
  synchronized with their installation's helper. Do not generate or offer ZIP,
  source or checksum bundles unless requested. Preserve runtime data and MIT attribution.
- No automatic drivers, HDR subsystem, custom display driver or full rollback installer.

## Evidence boundaries

Native DrawToBitmap fixtures document layout and state, not live session or OS DPI
acceptance. The current native review ships the scored host-copy-feedback correction;
it does not certify every behavior. Automated checks and source review remain
separate from native acceptance.

Live acceptance loaded Google in isolated child Chrome beside normal Chrome, and
rendered Steam after an explicitly authorized main-session handoff. Metro Exodus
rendered its menu and loading screen. After human UAC approval, separate main and
child Sunshine processes listened concurrently on different ports. Real-client
streaming, gameplay/controller/audio behavior, tray restore/reconnect and Wallpaper
Engine playback/monitor mapping remain separate acceptance work.
Do not promote process, encoder or listener discovery into a passed
stream/game/hardware claim.

## Product principles

Make the session being controlled unambiguous. Show unknown states honestly.
Make recovery actionable. Keep the streaming protocol with the selected host and client.
Keep the native desktop workspace central and the controls quiet and readable.

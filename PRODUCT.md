# Product

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop, native WinForms. This platform is outside the skill's web/iOS/Android taxonomy; retain the actual Windows stack and native conventions.

## Users and purpose

PC users who want to stream games or applications from a second Windows desktop
while continuing to use the physical desktop. The approved flow keeps portable
Sunshine inside a Windows child session, with ChildStream holding the RDP viewer
open so that desktop can be captured.

## Operating context

x64 Windows 10/11 Pro, .NET Framework, GPU hardware encoding, and a compatible
Moonlight-family client on another device. Apollo and VibePollo are hosts rather
than clients. Existing installed hosts can coexist on separate ports; this app
does not alter their services or promise their fork-specific features.

## Confirmed capabilities and constraints

- Native GUI and tray, session/reconnect controls, session-scoped host status.
- Ask before starting Sunshine; never start it on the physical desktop as a fallback.
- Initial elevated setup remains separate from ordinary app use.
- No automatic drivers, HDR subsystem, custom display driver, or full rollback installer.
- Build a Windows executable and portable/source bundles; preserve original MIT attribution.
- Test thoroughly, while stating which hardware/client checks were actually performed.

## Product principles

Make the session being controlled unambiguous. Show unknown states honestly.
Make recovery actionable. Keep the streaming protocol with Sunshine and the client.
Keep the native desktop workspace central and the controls quiet and readable.

# Compatibility and acceptance matrix

ChildStream creates a separate Windows child desktop and manages the portable
Sunshine executable at `Sunshine\Sunshine\sunshine.exe`. It does not take over
a console Sunshine installation. Pair explicitly with the child host's configured
base port (new setups: 48989); its settings interface uses base port + 1.

| Target | Role | Expected integration | Evidence for this revision |
| --- | --- | --- | --- |
| Moonlight PC | Client | Standard Sunshine pairing/video/audio/input | Protocol unchanged; device smoke test pending |
| Moonlight Android | Client | Standard Sunshine pairing/video/audio/input | Protocol unchanged; device smoke test pending |
| Moonlight iOS/tvOS | Client | Standard Sunshine pairing/video/audio/input | Protocol unchanged; device smoke test pending |
| Artemis Android | Client | Sunshine-compatible connection mode | Client fork; device/extension smoke test pending |
| Moonlight TV | webOS/embedded client | GameStream pairing and compatible codecs | Device smoke test pending |
| Apollo | Host | Separate console installation may coexist on separate ports | Managed-host replacement is outside this release |
| VibePollo | Host | Separate console installation may coexist on separate ports | Managed-host replacement is outside this release |

These are compatibility expectations based on the projects' roles, not results
of end-to-end testing. ChildStream does not modify Sunshine's streaming protocol.
Version-specific host/client extensions, virtual displays, HDR, controller drivers,
codec support and encoder selection require their own tests.

For a hardware acceptance run, record the Windows build, GPU/driver, Sunshine and
client versions, configured base port, codec, resolution and refresh rate. Check:

1. Child creation/authentication and parent desktop usability.
2. Decline the stopped-host prompt, wait through polls, then start explicitly.
   Cancel UAC, retry and approve inside the child. Check missing/helper/crash recovery.
3. Pair the correct host, launch a game, test video, audio, keyboard, mouse and
   gamepad with its appropriate input driver.
4. Minimize/restore, reconnect, disconnect a client and restart Sunshine; record
   capture/input behavior, latency and any frame drops.
5. Keep unrelated console/RDP sessions open and verify identity checks/sign-out
   target only the actual child. Use disposable child applications for sign-out.
6. Check 100%, 150%, 200% Windows scaling, high contrast and assistive technology.

Automated native-boundary tests and synthetic GUI captures cover policy and layout,
not this matrix. See [the review report](code-review.md) for additional edge cases
and [the README](../README.md#clients-and-host-forks) for current project links.

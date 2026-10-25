# Compatibility and acceptance evidence

ChildStream manages one Windows child desktop for the same Windows user. It can
start a separate Sunshine/Apollo instance with child-only settings and a complete
free TCP/UDP port family. It does not modify streaming protocols or guarantee
universal application compatibility.

## Live acceptance on 2026-10-17

Environment: Windows 11 Pro 10.0.26300/build 26300; NVIDIA GeForce RTX 2060 SUPER,
driver 32.0.16.1088; console session 1, actual Windows child session 2. These are
observations on one development PC, not results on every supported Windows build.

| Component | Version | Observed result | Remaining acceptance |
| --- | --- | --- | --- |
| ChildStream | Current review/reliability source | Connected viewer and matching installed helper; prompt declined without repeated polling prompts; helper replaced without signing out child | Fresh Windows setup, real no-host installation, additional DPI/accessibility and unrelated-session sign-out tests |
| Sunshine | 2026.914.233613 | Main PID 1956 remained in console; separate child PID 25556 started after human UAC approval; main TCP 47984/47989/47990/48010 and child TCP 48984/48989/48990/49010 listened concurrently | First-use web account, client pairing, child display capture, audio, encoder load and simultaneous streams |
| Chrome | 155.0.8059.80 | Main browser stayed open; child process used Chrome-Child profile; actual Google page loaded in child | Sign-in, media/DRM and browser extensions |
| Steam | 11.12.30.07 | Cross-session conflict preserved main Steam; after explicit user-approved handoff, Steam PID 12016 in child rendered store and library | Account reauthentication, other launchers, concurrent different-user configurations |
| Metro Exodus | 1.0.0.7 | Launched from child Steam; splash, animated menu and Winter loading screen rendered in child | Gameplay, sustained performance, audio and controller over a real client; test process cleaned up afterward |
| Wallpaper Engine | 2.8.0.42 | Main and child wallpaper64 processes coexist; child desktop showed the static Windows wallpaper, not the configured web wallpaper | Child configuration UI, RDP monitor mapping, selected wallpaper playback and effect on shared settings |
| Apollo | No installed build tested | Discovery/profile/config boundaries covered by automated tests | Real startup, capture and coexistence on a specified build |
| VibePollo | No installed build tested | Detected; independent child start explicitly refused because shared helper/display state is unverified | Separate integration/coexistence design and real host tests |

Two live defects were reproduced and fixed: the child helper incorrectly looked
for a child of its own session, rejecting app/host requests; the correct installation
was reported as a different folder when one path had a final separator. Parent
per-request Windows child lookup and native peer validation remain in place.

Minimizing and restarting the viewer preserved both hosts/browsers and child Steam.
The native tool could not target the hidden tray window for restore; final reconnect
interaction was deferred when the user resumed a foreground game. Do not count
either control as a passed end-to-end test. Wallpaper Engine's existing shared
configuration includes safe mode and remote-monitor entries, but those observations
do not establish the cause of its missing child wallpaper; no shared settings were
changed. An ambiguous child tray icon opened Windows Security, where automation
stopped without changing settings.

## Clients

| Client | Integration expectation | Actual end-to-end result |
| --- | --- | --- |
| Moonlight PC | Standard Sunshine pairing/video/audio/input | Pending device test |
| Moonlight Android | Standard Sunshine pairing/video/audio/input | Pending device test |
| Moonlight iOS/tvOS | Standard Sunshine pairing/video/audio/input | Pending device test |
| Artemis Android | Compatible connection mode; fork extensions vary | Pending device test |
| Moonlight TV/webOS | Compatible pairing and supported codecs | Pending device test |

These are expected protocol relationships, not tested client certifications.
Custom host ports, HDR, virtual displays, controller drivers, codec support and
encoder selection depend on the specific host/client versions.

## Reproducible checks and gaps

`scripts/test.ps1` exercises 150 C# policy/native-boundary cases, 7 PowerShell cases
and 3 temporary packaging cases. Both Windows PowerShell 5.1 and PowerShell 7 run
the same suite. It does not launch real apps/hosts, change Windows settings or log
off sessions. Synthetic native captures establish fixture layout only.

For each hardware test record Windows/GPU/driver, host and client versions, port,
codec, resolution/refresh, and separate results for pairing, capture, video, audio,
keyboard/mouse, gamepad, minimize/restore and reconnect. Use disposable apps for
sign-out tests. Authentication, UAC and certificate warnings require human handling.
Pending cases above must not be marked passed from a process or TCP listener alone.

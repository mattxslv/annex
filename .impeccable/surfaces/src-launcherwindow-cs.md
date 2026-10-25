---
version: 1
slug: "src-launcherwindow-cs"
primary_target: "src/LauncherWindow.cs"
related_targets: ["src/LauncherApplication.cs", "src/LauncherPolicy.cs", "src/ChildAppWindow.cs"]
---

# Native launcher surface

Mode: Operate. Existing Windows viewer extension, code-led, with the approved
pale-slate/blue native direction; no raster comp or visual-world tournament.

## Direction contract

THESIS: A second desktop with a visible host state, always clear about which
session is controlled. The viewer remains the working surface.

OWN-WORLD: Pale slate #EFF3F8, white control surface, ink #182838, muted #526579,
blue #215CDD. Segoe UI with native chrome, text-labelled status and accessible focus.
High contrast follows Windows system colors. A daylight desktop task benefits from
a light, readable surface rather than a game-themed neon dashboard.

STORY: Set up and start the second desktop independently of a streaming host.
Optionally select an installed host, explicitly start its child instance and pair
a compatible client. Open installed apps in the child and minimize the attached viewer.

FIRST VIEWPORT: A 300-pixel companion column contains session/host status and the
primary actions. The larger right workspace holds the RDP desktop or concise
first-run guidance, with Open app in its toolbar and pairing below. Setup / repair
remains in the footer outside the scrolling rail. No decorative cards are introduced.

FORM: Native viewer with a companion control column; precisely scoped extension
of the incumbent WinForms surface. No seed key: the user-approved design fixes the form.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Approved host/app extension (2026-10-17)

Desktop setup and host discovery are independent. No host is an optional empty
state; helper failure is a separate recovery state. The selected installation
names its actions. Keep Start host above secondary Refresh/Browse controls at the
minimum window; Setup / repair stays in the status bar. The native app picker
lists registered EXEs and reports dispatch separately from verified app UI.
Chrome/Edge use persistent child-only profiles. Remote keyboard routing is applied
before every RDP connection; click the companion column to leave viewer focus.

Author QA: two synthetic DrawToBitmap fixture rounds, stopped/multiple hosts,
missing host, app picker and expanded pairing at compact size. The first round
found Start host below the fold; a failing minimum-window regression now pins its
visible placement. Existing palette/type/system-color direction is preserved.
Actual Windows DPI and live stream/game acceptance remain separate from fixtures.

## Current post-review handoff (2026-10-17)

The native review disposition is ship for its one scored generic host-copy-feedback
correction; the original full fixture review found no other material layout issue.
Checked `.impeccable/review/desktop.png`, `desktop-compact.png`, `apps.png`,
`pairing.png` and `pairing-compact.png`. All are explicit synthetic DrawToBitmap
fixtures. Their provenance is review evidence, not product raster artwork or live
RDP, browser, game, hardware or OS DPI acceptance.

The current source keeps setup/helper functionality inside the EXE and makes a
streaming host optional. Discovery lists Sunshine, Apollo and VibePollo; independent
VibePollo startup stays unsupported pending coexistence verification. Supported
hosts use separate child profiles and ports. Refresh/Browse/install links remain
secondary to the selected-host action. Both pairing copy actions require a running,
attached child host. Host web UI copy/browser feedback is provider-neutral.

The app picker reports launch dispatch separately from rendered UI. Chrome/Edge
keep child-only data, Steam requires a user-directed handoff if another desktop
owns the client, and Wallpaper Engine can share settings. Remote keyboard routing
is applied at connection time with a visible failure diagnostic.

The default deliverable is ChildStream.exe in its existing installation, with its
matching helper. No bundles are generated or offered unless requested. Live tests
loaded Google in child Chrome, rendered Steam and Metro Exodus menu/loading screens,
and started a separate listening child Sunshine after human UAC approval. Those
observations do not certify gameplay, a client stream, controller/audio behavior,
tray restore/reconnect or Wallpaper Engine mapping.
Keep those acceptance steps separate and preserve the incumbent visual world.

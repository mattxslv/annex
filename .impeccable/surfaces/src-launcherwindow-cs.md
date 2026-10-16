---
version: 1
slug: "src-launcherwindow-cs"
primary_target: "src/LauncherWindow.cs"
related_targets: ["src/LauncherApplication.cs"]
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

STORY: Start the second desktop, see whether Sunshine is running there, explicitly
start it if off, pair a compatible client, and minimize the attached viewer.

FIRST VIEWPORT: A 300-pixel companion column contains session/host status and the
primary actions. The larger right workspace holds the RDP desktop or concise
first-run guidance. Setup and pairing remain visible without decorative cards.

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

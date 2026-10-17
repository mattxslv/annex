---
name: ChildStream
description: A quiet native Windows companion for a second desktop.
colors:
  action-blue: "#215CDD"
  action-text: "#FFFFFF"
  workspace-slate: "#EFF3F8"
  ink: "#182838"
  muted-ink: "#526579"
  divider: "#D9E2EC"
  control-border: "#C3CFDD"
  disabled-surface: "#E9EEF4"
  running: "#146847"
  attention: "#B42335"
typography:
  brand:
    fontFamily: "Segoe UI"
    fontSize: "21pt"
    fontWeight: 700
  section:
    fontFamily: "Segoe UI"
    fontSize: "10pt"
    fontWeight: 700
  status:
    fontFamily: "Segoe UI"
    fontSize: "14pt"
    fontWeight: 700
  body:
    fontFamily: "Segoe UI"
    fontSize: "9.5pt"
    fontWeight: 400
  workspace-title:
    fontFamily: "Segoe UI"
    fontSize: "11pt"
    fontWeight: 700
  guidance-title:
    fontFamily: "Segoe UI"
    fontSize: "24pt"
    fontWeight: 700
  guidance-body:
    fontFamily: "Segoe UI"
    fontSize: "11pt"
    fontWeight: 400
  field-label:
    fontFamily: "Segoe UI"
    fontSize: "9.5pt"
    fontWeight: 700
spacing:
  workspace-inset: "24px"
  rail-vertical-inset: "22px"
  control-gap: "6px"
  workspace-row-gap: "14px"
  section-divider-gap: "17px"
components:
  button-primary:
    backgroundColor: "{colors.action-blue}"
    textColor: "{colors.action-text}"
    typography: "{typography.body}"
    padding: "3px 10px"
  button-secondary:
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    padding: "3px 10px"
  button-disabled:
    backgroundColor: "{colors.disabled-surface}"
    textColor: "{colors.muted-ink}"
    typography: "{typography.body}"
    padding: "3px 10px"
  address-field:
    textColor: "{colors.ink}"
    typography: "{typography.body}"
  host-status:
    textColor: "{colors.ink}"
    typography: "{typography.status}"
  host-status-running:
    textColor: "{colors.running}"
    typography: "{typography.status}"
---

# Design System: ChildStream

## Overview

**Creative North Star: "The native desktop companion"**

ChildStream uses a quiet, readable Windows surface around the desktop it controls.
The native window, familiar controls and restrained palette support a working
desktop rather than competing with it. Session identity and explicit text status
carry the hierarchy; color reinforces that information.

This documents the implemented WinForms extension in `src/LauncherWindow.cs`,
`src/ChildAppWindow.cs`, `src/LauncherPolicy.cs` and `src/LauncherApplication.cs`,
under the approved direction in `.impeccable/surfaces/src-launcherwindow-cs.md`,
the host/app compatibility design and `PRODUCT.md`. It is a native design system; the
HTML/CSS snippets in `.impeccable/design.json` are documentation previews for the
design panel and are not product components or an implementation framework.

**Key Characteristics:**

- Native Windows chrome, controls, focus rendering and confirmation dialogs.
- Pale slate surround, system window surfaces, dark text and blue primary actions.
- A dominant desktop workspace with a compact companion control rail.
- Text-labelled states, actionable recovery and explicit synthetic-preview labels.

The 2026-10-17 native review disposition is **ship** for its one scored generic
host-copy-feedback correction. The original full fixture review found no other
material layout issue. Current reviewed evidence comprises
`.impeccable/review/desktop.png`, `desktop-compact.png`, `apps.png`, `pairing.png`
and `pairing-compact.png`: explicit synthetic DrawToBitmap fixtures, not live
desktop, browser, game or OS DPI evidence. Earlier finish and pairing review
reports remain history rather than a certification of this extension.

The implementation ledger records four Important code-review findings reproduced
RED, then 148 C# + 7 PowerShell + 3 packaging cases GREEN. Subsequent live-helper
and normalized-installation-path regressions reached 150 C# + 7 PowerShell + 3
packaging cases GREEN. This documentation pass does not rerun those suites or
complete native acceptance. Actual OS DPI transitions,
high contrast, assistive technology, real-client streaming, game rendering,
controller/audio behavior and Wallpaper Engine monitor mapping remain unvalidated
by these fixtures. Live app evidence and pending steps are recorded separately in
`PRODUCT.md` and the compatibility documentation.

## Colors

The fixed palette combines cool slate and dark ink with a clear blue action.
Frontmatter values are the implemented normal-mode colors, not replacements for
Windows system colors.

### Primary

- **Action blue** (`action-blue`): enabled primary button background and border.
- **Action text** (`action-text`): white text on enabled primary actions.

### Neutral

- **Workspace slate** (`workspace-slate`): the form and workspace surround.
- **Ink** (`ink`): primary text, ordinary state headings and secondary-button text.
- **Muted ink** (`muted-ink`): explanatory text, section labels and disabled text.
- **Divider** (`divider`): the rail separator and disabled-button border.
- **Control border** (`control-border`): enabled secondary-button outlines.
- **Disabled surface** (`disabled-surface`): disabled buttons in normal mode.
- **System window surface**: rail, viewer, guidance panel, secondary buttons and
  read-only address use `SystemColors.Window`. It appears white in the captured
  configuration; it is deliberately not frozen as a white palette token.

Status accents have narrow meanings: **running** is the selected host's running
heading; **attention** is used for error headings. Other host headings use ordinary
ink, including optional No host detected, checking and helper-unavailable states.

**The Session Truth Rule.** Always name the state in text. A green heading alone
cannot communicate which session or host is running.

High contrast is a native branch: text uses `SystemColors.WindowText`, surfaces
use `SystemColors.Window`, and buttons use standard Windows rendering. The blue
field's high-contrast fallback is `SystemColors.Highlight`; custom button styling
returns early in that mode. Semantic green/red headings also yield to system text.

## Typography

Segoe UI is the sole application font. Source sizes are points, as passed to
`System.Drawing.Font`; layout dimensions are WinForms units at the 96-DPI baseline.
No custom line-height, tracking, web font, monospaced treatment or fallback stack
is specified. Windows provides font metrics and text rendering.

The hierarchy in frontmatter follows the actual roles: bold brand, section and
status labels; regular body/control text; a smaller workspace heading; and a
larger guidance title with readable supporting copy. Client-address and Host web
UI labels use the body size in bold. The app picker inherits the body font and
native list/header/control rendering. Preserve sentence case and concise
action labels.

**The Native Rendering Rule.** Keep native text metrics, mnemonics and visible
focus behavior. Do not translate a web typography convention into a new native
font or decorative heading style.

## Layout

The baseline client area is (1180 x 760); the form minimum size is (900 x 640),
which includes non-client window chrome. `AutoScaleMode.Dpi` uses a (96, 96)
baseline. A two-column `TableLayoutPanel` assigns an absolute companion rail
(300) and the remaining width to the desktop workspace. There is no mobile
breakpoint or alternate navigation layout in this implementation.

The rail scrolls vertically when necessary. Its horizontal inset matches
`workspace-inset`, with `rail-vertical-inset` above and below. Rail labels normally
wrap within (252). A thin separator with `section-divider-gap` above and below
separates the desktop and streaming-host groups. Controls auto-size; buttons have
`control-gap` bottom and right margins. In the host group, the chooser and primary
Start action precede explanatory detail, Refresh/Browse and installation links so
the selected-host action stays reachable at the minimum window. Extra rail content
scrolls; it does not become a separate navigation surface.

The right column uses `workspace-inset` on every side. Its toolbar auto-sizes and
wraps; the central viewer consumes the remaining height. Below the viewer, a
three-column native table aligns the Client address and Host web UI rows:
auto-sized labels, a stretching field column and auto-sized copy actions. Labels
have a minimum width (112). Both text boxes dock to fill the middle column;
their constructor widths are initial values rather than runtime width invariants.
The table uses some of the nominal right inset in the compact and explicitly
scaled captures; all action labels remain visible in the reviewed scope.

The two row gaps use `workspace-row-gap`. The pairing toggle and optional guide
span all three columns. The read-only, word-wrapped guide has a bounded height
(170) and native vertical scrolling, preserving a useful compact viewer area.
First-run guidance uses an inset (38), a vertically centered message group and
width recalculation on resize; the guidance container can scroll when its message
exceeds the available area. The actual RDP control docks to fill the same workspace.
A native status strip spans the window bottom, keeping Setup / repair available
outside the scrolling rail.

The owned app-picker window has a baseline client area (620 x 370), minimum form
size (480 x 330) and native inset (18). A two-column App/Location list takes the
remaining height between introductory copy, a scrollable result area (76) and a
wrapping action row. These are this picker's observed dimensions, not new global
spacing or breakpoint tokens.

Preserve this workspace dominance when extending the current launcher. Treat
these measurements as the observed surface layout, not a general-purpose grid
for unrelated future Windows surfaces.

## Elevation & Depth

The application adds no custom shadows, gradients, animation or elevated cards.
Depth comes from the native window chrome, system control borders and the tonal
difference between the slate surround and window surfaces. The operating system
owns any window shadow, button press treatment and dialog rendering; no shadow
or motion token is extracted from those behaviors.

**The Flat Workspace Rule.** Keep application containers flat and let the desktop
remain the working surface.

## Shapes

Application panels and flat buttons are rectangular. There is no custom corner
radius scale. Normal-mode buttons use `FlatStyle.Flat` with a simple outline;
high contrast uses `FlatStyle.Standard`. The address field uses the native
`FixedSingle` border. Window corners, resize frame and title-bar buttons belong
to Windows rather than a custom shape system.

## Components

### Buttons

Buttons are familiar and direct. Their inherited body font, auto-sizing and
internal padding are captured in frontmatter. Primary controls have a minimum
height (38); secondary controls have a minimum height (30). The button border
matches action blue for an enabled primary, control border for an enabled
secondary, and divider for a disabled control.

Start desktop and Start [selected host] use the primary treatment when available.
Once attached, Show desktop becomes secondary. Host state and selection determine
whether Start [host] or Open [host] settings is visible; checking and waiting show
neither. Open app joins the workspace toolbar. Busy/ending state disables the relevant actions. The normal disabled
treatment is shared by primary and secondary buttons; minimum height still
follows the button's original primary flag.

Hover, press and focus come from WinForms; the source does not set custom
hover/pressed colors or transitions. Preserve native focus cues and the existing
mnemonics. Main buttons have explicit accessible names with mnemonic markers
removed. The desktop action updates its accessible name from Start desktop to
Show desktop with its visible label. End session uses a restrained secondary
treatment and an ellipsis, followed by a native confirmation whose default is No.

### Host status

The heading explicitly distinguishes Waiting for desktop, Checking [host],
[host] is off, [host] running, Starting [host], No host detected, Helper unavailable,
Needs attention and Status unknown. Supporting copy explains the
same child session and a recovery step when applicable. Visible host detail is
limited to (230) characters; the complete message remains in its tooltip and
accessible description.

A newly attached child session shows a checking state with no inherited address.
Routine polls retain the useful same-session snapshot to avoid periodic flicker.
Running uses the running color; errors use attention. No host detected is an
optional-host empty state, distinct from Windows setup or a failed helper. All
states remain distinguishable without those accents.

The native drop-down appears for multiple discovered installations and includes
host name and location; its tooltip includes the selected path and version. Refresh
and Browse are secondary actions enabled for the attached, nonbusy child. Install
a host opens a native vendor menu for Sunshine, Apollo and VibePollo. Detection does
not imply supported startup: VibePollo remains explicitly unsupported for independent
child startup pending coexistence verification. These controls retain native
selection, focus and menu behavior.

### Client address

The read-only native text box stretches in the table's middle column, with ink
text and system window background. It has the accessible name Client pairing
address and a tooltip explaining LAN-IP fallback. A Running snapshot with a valid
port produces the machine name and port; other states say Available when host is
running. Copy address requires an attached, running target and is disabled while
ending. A stopped host no longer exposes a copyable pairing address. Successful
copy and clipboard failure both receive honest footer feedback.

### Host web UI and pairing instructions

The second read-only field shares the address-field treatment and has the
accessible name of the selected host's web UI URL, or Host web UI URL when no host
is selected. A Running snapshot with a validated base
port produces only the local `https://127.0.0.1:<base+1>/` URL. Other states show
Available when host is running. Copy settings URL requires a connected,
running target with a valid URL and is disabled while ending; it is disabled in
stopped, unavailable and checking states. Open [host] settings also waits
for busy work to finish and revalidates the actual child session before sending
the browser request.

Show pairing steps expands the native read-only, scrollable instructions below
both rows; Hide pairing steps collapses them. The toggle's accessible name follows
its visible label. Windows CRLF paragraph boundaries separate the four numbered
steps. Their copy directs the browser to the normal desktop, distinguishes the
selected host's web account from the Windows password, and explains the client PIN
handoff into this child host instance. Later steps remain available through
the bounded guide's native vertical scrollbar. Unknown-host guidance explains
the prerequisite rather than showing a fabricated URL.

### Child app picker

Open app opens an owned native window with an App/Location ListView, a read-only
scrollable result field and Open in child, Browse app... and Refresh apps buttons.
Open in child is disabled until one available registered app is selected. Browse
uses a native EXE picker. This window uses system window/text colors and native
visual-style buttons, rather than introducing a second accent or card style.

Result copy distinguishes dispatch from verified app UI. Chrome/Edge use persistent
child-only data; Steam can require an explicit user handoff from another desktop;
Wallpaper Engine settings can also affect the main desktop. Keep these limitations
visible rather than styling a returned process as proven compatibility.

### Viewer and guidance

The plain system-window panel, named DesktopWorkspace with the accessible name
Child desktop workspace, contains either the docked native RDP control or concise
first-run/setup guidance. Guidance is an empty-state message, not a card
or decorative illustration. Synthetic previews explicitly identify themselves
in the title bar, central message and footer; their blank white workspace is an
intentional placeholder rather than evidence of a live desktop.

### Native shell

The status strip presents short feedback alongside the always-visible Setup /
repair action. That action invokes this EXE's elevated desktop setup; a streaming
host is optional and is not downloaded by setup. A successful shell dispatch says
Browser requested and offers the Host web UI URL as a manual fallback. Successful
copy says Host web UI URL copied. Neither promises that a browser window became
visible. The tray uses native `NotifyIcon`, `ContextMenuStrip`, application icon
and balloon tips. Native message boxes handle selected-host approval, sign-out
confirmation and errors. Preserve these Windows
interaction conventions rather than adding an unrelated navigation or dialog
design language.

## Do's and Don'ts

### Do:

- **Do** keep the native desktop workspace visually dominant.
- **Do** identify the child session and host state in readable text.
- **Do** use Segoe UI and preserve Windows focus, mnemonics and system-color overrides.
- **Do** show the selected host's checking state distinctly while the first child-session status is pending.
- **Do** label synthetic previews and retain their evidence limits.

### Don't:

- **Don't** introduce decorative cards, neon game-dashboard styling or a second font into this approved surface.
- **Don't** use color as the sole signal for running, errors or unavailable state.
- **Don't** replace native high-contrast colors with the normal-mode palette.
- **Don't** invent custom radius, shadow, hover, motion or breakpoint tokens absent from the implementation.
- **Don't** describe explicit layout scaling or synthetic captures as live streaming or OS DPI validation.

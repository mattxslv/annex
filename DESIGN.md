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
    width: "210px"
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

This documents the implemented WinForms extension in `src/LauncherWindow.cs` and
`src/LauncherApplication.cs`, under the approved direction in
`docs/launcher-surface.md` and `PRODUCT.md`. It is a native design system; the
HTML/CSS snippets in `.impeccable/design.json` are documentation previews for the
design panel and are not product components or an implementation framework.

**Key Characteristics:**

- Native Windows chrome, controls, focus rendering and confirmation dialogs.
- Pale slate surround, system window surfaces, dark text and blue primary actions.
- A dominant desktop workspace with a compact companion control rail.
- Text-labelled states, actionable recovery and explicit synthetic-preview labels.

The finish verdict is **ship** after correction of the initial checking state; see
`.impeccable/review/finish-review.md` and `finish-verdict.md`. Visual evidence is
the synthetic stopped, running, unavailable, checking, compact and large-layout
captures in that directory. The large-layout capture uses explicit 1.5x layout
scaling and does not validate OS DPI behavior. Live RDP/streaming, Windows
high-contrast rendering, assistive technology and OS DPI transitions remain
outside that review's validation.

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

Status accents have narrow meanings: **running** is the Sunshine-running heading;
**attention** is used for error and setup-required headings. Other host headings
use ordinary ink, including checking and unavailable states.

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
larger guidance title with readable supporting copy. Client-address labels use
the body size in bold. Preserve sentence case and concise action labels.

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
`control-gap` bottom and right margins.

The right column uses `workspace-inset` on every side. Its toolbar and pairing row
auto-size and wrap; the central viewer consumes the remaining height. The two
row gaps use `workspace-row-gap`. First-run guidance uses an inset (38), a
vertically centered message group and width recalculation on resize. The actual
RDP control docks to fill the same workspace. A native status strip spans the
window bottom.

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

Start desktop and Start Sunshine use the primary treatment when available.
Once attached, Show desktop becomes secondary. Host state determines whether
Start Sunshine or Open Sunshine settings is visible; checking and waiting show
neither. Busy/ending state disables the relevant actions. The normal disabled
treatment is shared by primary and secondary buttons; minimum height still
follows the button's original primary flag.

Hover, press and focus come from WinForms; the source does not set custom
hover/pressed colors or transitions. Preserve native focus cues and the existing
mnemonics. Main buttons have explicit accessible names with mnemonic markers
removed. End session uses a restrained secondary treatment and an ellipsis,
followed by a native confirmation whose default is No.

### Host status

The heading explicitly distinguishes Waiting for desktop, Checking Sunshine,
Sunshine is off, Sunshine running, Starting Sunshine, Setup required, Helper
unavailable, Needs attention and Status unknown. Supporting copy explains the
same child session and a recovery step when applicable. Visible host detail is
limited to (230) characters; the complete message remains in its tooltip and
accessible description.

A newly attached child session shows Checking Sunshine with no inherited address.
Routine polls retain the useful same-session snapshot to avoid periodic flicker.
Running uses the running color; setup required and error use attention. All
states remain distinguishable without those accents.

### Client address

The read-only native text box uses the frontmatter width, ink text and system
window background. It has the accessible name Client pairing address and a
tooltip explaining LAN-IP fallback. A validated host port produces the machine
name and port; otherwise the field says Available after host check. A separate
Copy address button provides clipboard feedback in the footer when a valid
address can be copied. It currently remains enabled for an unavailable address
and silently returns; the finish review records this as optional deferred polish.

### Viewer and guidance

The plain system-window panel contains either the docked native RDP control or
concise first-run/setup guidance. Guidance is an empty-state message, not a card
or decorative illustration. Synthetic previews explicitly identify themselves
in the title bar, central message and footer; their blank white workspace is an
intentional placeholder rather than evidence of a live desktop.

### Native shell

The status strip presents short feedback. The tray uses native `NotifyIcon`,
`ContextMenuStrip`, application icon and balloon tips. Native message boxes handle
start-host approval, sign-out confirmation and errors. Preserve these Windows
interaction conventions rather than adding an unrelated navigation or dialog
design language.

## Do's and Don'ts

### Do:

- **Do** keep the native desktop workspace visually dominant.
- **Do** identify the child session and host state in readable text.
- **Do** use Segoe UI and preserve Windows focus, mnemonics and system-color overrides.
- **Do** show Checking Sunshine distinctly while the first child-session status is pending.
- **Do** label synthetic previews and retain their evidence limits.

### Don't:

- **Don't** introduce decorative cards, neon game-dashboard styling or a second font into this approved surface.
- **Don't** use color as the sole signal for running, errors or unavailable state.
- **Don't** replace native high-contrast colors with the normal-mode palette.
- **Don't** invent custom radius, shadow, hover, motion or breakpoint tokens absent from the implementation.
- **Don't** describe explicit layout scaling or synthetic captures as live streaming or OS DPI validation.

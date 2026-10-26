# Details

## How it works

Windows Pro can run one *child session* per user: a second logon session that lives inside your own.
Annex enables it, signs it in through the built-in RDP control over loopback, and shows it in its window.
A small helper starts with that session and runs Sunshine there when you click **Start Sunshine**.
The child session only has a display while Annex is attached, which is why Annex has to stay open.

## What setup changes

- Enables child sessions, allows RDP over loopback only (the Remote Desktop firewall rules stay off),
  and raises the session frame rate (`DWMFRAMEINTERVAL`).
- Adds `annex-helper.cmd` to your Startup folder and a firewall rule for Annex's Sunshine.
- Makes the `Sunshine` folder admin-write only. Sunshine runs as administrator (so input reaches games
  that do too), and this keeps other programs from changing what it runs. Edit its settings through
  the Sunshine web page.

The original values are saved in `setup-state.json`; uninstalling puts them back.

## Resolution and scale

The Annex desktop is 1920x1080 at 100% by default. To match your device, create `display.cfg` next to
`Annex.exe` with one line, for example `2560x1600x200` (width x height x scale %), then **Reconnect**.

## Troubleshooting

- **"Helper is from a different or older Annex.exe"**: you updated Annex while an Annex desktop was
  signed in. Click **End session**, then **Start desktop**.
- **"Started without administrator rights"**: Sunshine's folder isn't protected, so games running as
  administrator won't get input. Run setup again.
- **Sign-in fails**: use your Windows account password, not the Windows Hello PIN.
  Tray menu → **Forget saved password** clears a saved one.
- Logs: `launcher.log`, `autostart.log` and `Sunshine\Sunshine\config\sunshine.log` in the Annex folder
  (`%LOCALAPPDATA%\Programs\Annex` when installed).

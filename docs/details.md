# Details

## How it works

Windows Pro can run one *child session* per user: a second logon session that lives inside your own.
Annex enables it, signs it in through the built-in RDP control over loopback, and shows it in its window.
A small helper starts with that session and runs Sunshine there when you click **Start Sunshine**.
The child session only has a display while Annex is attached, which is why Annex has to stay open.

## What setup changes

- Enables child sessions, allows RDP over loopback only (the Remote Desktop firewall rules stay off),
  and raises the session frame rate (`DWMFRAMEINTERVAL`).
- Turns on the policy *Use hardware graphics adapters for all Remote Desktop Services sessions*
  (`bEnumerateHWBeforeSW`). Without it Windows 10 draws the Annex desktop on the CPU, which makes it
  slow and laggy.
- Adds `annex-helper.cmd` to your Startup folder and a firewall rule for Annex's Sunshine.
- Makes the `Sunshine` folder admin-write only. Sunshine runs as administrator (so input reaches games
  that do too), and this keeps other programs from changing what it runs. Edit its settings through
  the Sunshine web page.
- Optional (*route audio*): sends Annex's own sound to the silent Steam Streaming Speakers, so the Annex
  desktop's audio plays only on your client. Uninstalling removes this.

The original values are saved in `setup-state.json`; uninstalling puts them back. If Windows setup does not
finish (for example an administrator prompt was declined), the installer says so; choose **Setup / repair** in Annex.

## Resolution and scale

The Annex desktop is 1920x1080 at 100% by default. To match your device, create `display.cfg` next to
`Annex.exe` with one line, for example `2560x1600x200` (width x height x scale %), then **Reconnect**.

## Troubleshooting

- **"Helper is from a different or older Annex.exe"**: you updated Annex while an Annex desktop was
  signed in. Click **End session**, then **Start desktop**.
- **"Started without administrator rights"**: Sunshine's folder isn't protected, so games running as
  administrator won't get input. Run setup again.
- **Sign-in fails**: use your Windows account password, not the Windows Hello PIN.
  - **Microsoft account**: use your Microsoft account password. If you have only ever signed in with a
    PIN on this PC, lock the PC and sign in once with the password so Windows learns it.
  - If **Settings → Accounts → Sign-in options → "For improved security, only allow Windows Hello sign-in
    for Microsoft accounts on this device"** is on, Windows refuses every password, so turn it off.
    Annex links to it when it's on.
  - **Account without a password**: press Save twice with an empty field. If Windows refuses it,
    set a password for the account.
  - When Windows refuses a saved password, Annex deletes it and asks again instead of retrying, so it
    can't lock the account. Tray menu → **Forget saved password** clears it by hand.
- **Slow or laggy Annex desktop (Windows 10)**: choose **Setup / repair** to turn on hardware graphics
  for the Annex desktop, then **End session** and **Start desktop**.
- **"Windows has the Annex helper turned off in Startup apps"**: turn `annex-helper` back on in
  Settings → Apps → Startup (or Task Manager → Startup apps), then **End session** and **Start desktop**.
- **"Sunshine stopped answering" after Apply or Restart on its web page**: Sunshine restarts itself only when
  it runs as a Windows service, which the Annex desktop's copy does not. Choose **Start** in Annex: it closes the
  stuck copy (Windows may ask for approval) and starts a fresh one.
- Logs: `launcher.log` (its `start:` line lists the Windows build, account type and these settings; attach it
  to issues), `autostart.log` and `Sunshine\Sunshine\config\sunshine.log` in the Annex folder
  (`%LOCALAPPDATA%\Programs\Annex` when installed).

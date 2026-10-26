using System;
using System.Globalization;
using System.Linq;

static class LauncherPolicy
{
    public static HostSnapshot ForSession(HostSnapshot previous, uint oldChild, uint newChild)
    {
        if (newChild == 0) return oldChild == 0 ? previous : new HostSnapshot(HostState.Waiting, "Attach the Annex desktop to check or start its streaming host.", 0);
        if (oldChild != newChild) return new HostSnapshot(HostState.Checking, "Checking the host in the attached Annex desktop...", 0);
        return previous;
    }
    public static HostSnapshot DiscoverySnapshot(HostDiscoveryResult found, bool attached) {
        if (found.Installations.Length == 0) {
            if (found.Diagnostics.Length != 0) return new HostSnapshot(HostState.Unknown, "Host discovery could not finish or verify an installation. Refresh or Browse to the host EXE. You can still start and use the Annex desktop.", 0);
            return new HostSnapshot(HostState.Missing, "No streaming host detected. Install Sunshine (the portable zip works) or Apollo to stream; you can still start and use the Annex desktop.", 0);
        }
        if (!found.Installations.Any(HostSelection.Usable))
            return new HostSnapshot(HostState.Missing, "No host for the Annex desktop yet. VibePollo is your main host and can't run a second copy here. Install Sunshine (the portable zip works); your main host stays as it is.", 0);
        return new HostSnapshot(attached ? HostState.Checking : HostState.Waiting, attached ? "Choose the host to use in the Annex desktop." : "A streaming host was detected. Start the Annex desktop to choose and run its separate instance.", 0);
    }
    public static string SettingsFeedback(bool copied) { return copied ? "Host web UI URL copied. Paste it into a browser on your normal desktop." : "Browser requested. If nothing opens, copy the Host web UI URL into your browser."; }
    public static bool CanStartHost(HostSnapshot status, bool connected)
    {
        return connected && HostConfiguration.IsValidPort(status.BasePort) && (status.State == HostState.Stopped || status.State == HostState.Error);
    }
    public static string SettingsUrl(HostSnapshot status)
    {
        return status.State == HostState.Running && HostConfiguration.IsValidPort(status.BasePort) ? "https://127.0.0.1:" + (status.BasePort + 1).ToString(CultureInfo.InvariantCulture) + "/" : null;
    }
    public static string PairingInstructions(string computerName, HostSnapshot status)
    { return PairingInstructions(computerName, status, "Sunshine"); }
    public static string PairingInstructions(string computerName, HostSnapshot status, string hostName)
    {
        string url = SettingsUrl(status);
        if (url == null) return ("Start the Annex desktop and " + hostName + " first, or wait for startup to finish. Resolve any setup or helper error shown above.\n\nPairing uses a browser on your normal desktop; a browser inside the Annex desktop is not required.").Replace("\n", Environment.NewLine);
        string address = computerName + ":" + status.BasePort.ToString(CultureInfo.InvariantCulture);
        return ("1. Open " + url + " in a browser on your normal desktop. No browser is needed inside the Annex desktop.\n\n" +
            "2. First time only: accept Sunshine's local certificate and create a Sunshine login. It is separate from your Windows password.\n\n" +
            "3. In Moonlight on your phone, tablet or TV, add " + address + ". If the name does not resolve, use this PC's LAN IP. Moonlight then shows a PIN.\n\n" +
            "4. In Sunshine's PIN tab, enter that PIN and a device name, then choose Desktop in Moonlight. Keep Annex open; minimize it to the tray while playing.").Replace("Sunshine", hostName).Replace("\n", Environment.NewLine);
    }
}

sealed class HostPromptPolicy
{
    uint child;
    bool asked;
    string selection;
    public bool ShouldAsk(uint session, HostState state, bool connected)
    { return ShouldAsk(session, "legacy", state, connected); }
    public bool ShouldAsk(uint session, string selected, HostState state, bool connected)
    {
        if (session != child || selected != selection) { child = session; selection = selected; asked = false; }
        if (state == HostState.Running) asked = false;
        if (!connected || session == 0 || string.IsNullOrEmpty(selected) || state != HostState.Stopped || asked) return false;
        asked = true;
        return true;
    }
}

static class RdpInputSettings
{
    public static string Apply(dynamic client) {
        // 2 = Windows key shortcuts go to the Annex desktop only in full screen, which the viewer never uses,
        // so Win+Shift+S, Win+D etc. keep working on the normal desktop.
        try { client.SecuredSettings2.KeyboardHookMode = 2; return null; }
        catch (Exception ex) { return HostInstallation.Limit("Remote keyboard routing could not be set. Use Alt+Home for the Annex Start menu; click the companion column to leave the viewer. " + ex.Message, 512); }
    }
    public static void Connect(dynamic client, Action<string> report) { string diagnostic = Apply(client); if (diagnostic != null) report(diagnostic); client.Connect(); }
}

sealed class SessionRequestGate
{
    uint child;
    bool connected;
    bool closed;
    string selection;
    public int Generation { get; private set; }
    public void Update(uint session, bool attached)
    {
        if (child != session || connected != attached) { child = session; connected = attached; Generation++; }
    }
    public bool Accepts(int generation, uint session) { return !closed && connected && session == child && generation == Generation; }
    public void Select(string identity) { if (selection != identity) { selection = identity; Generation++; } }
    public void Close() { closed = true; Generation++; }
}

// A modal dialog pumps timer events. Its approval belongs to the connection that
// opened it, even if Windows later reuses the same numeric child-session ID.
sealed class SessionConfirmation
{
    readonly SessionRequestGate gate;
    readonly int generation;
    public uint Child { get; private set; }
    public SessionConfirmation(SessionRequestGate gate, uint child)
    {
        this.gate = gate; generation = gate.Generation; Child = child;
    }
    public bool CanProceed(uint currentChild) { return currentChild == Child && gate.Accepts(generation, Child); }
}

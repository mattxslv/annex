using System;
using System.Globalization;

static class LauncherPolicy
{
    public static HostSnapshot ForSession(HostSnapshot previous, uint oldChild, uint newChild)
    {
        if (newChild == 0) return new HostSnapshot(HostState.Waiting, "Attach the child desktop to check or start its streaming host.", 0);
        if (oldChild != newChild) return new HostSnapshot(HostState.Checking, "Checking Sunshine in the attached child desktop...", 0);
        return previous;
    }
    public static bool CanStartHost(HostSnapshot status, bool connected)
    {
        return connected && HostConfiguration.IsValidPort(status.BasePort) && (status.State == HostState.Stopped || status.State == HostState.Error);
    }
    public static string SettingsUrl(HostSnapshot status)
    {
        return status.State == HostState.Running && HostConfiguration.IsValidPort(status.BasePort) ? "https://127.0.0.1:" + (status.BasePort + 1).ToString(CultureInfo.InvariantCulture) + "/" : null;
    }
    public static string PairingInstructions(string computerName, HostSnapshot status)
    {
        string url = SettingsUrl(status);
        if (url == null) return "Start the child desktop and Sunshine first, or wait for startup to finish. Resolve any setup or helper error shown above.\n\nPairing uses a browser on your normal desktop; a browser inside the child desktop is not required.".Replace("\n", Environment.NewLine);
        string address = computerName + ":" + status.BasePort.ToString(CultureInfo.InvariantCulture);
        return ("1. On this PC's normal desktop, open " + url + " in your browser. A browser inside the child desktop is not required.\n\n" +
            "2. First time: review Sunshine's local certificate and create a Sunshine web username/password. This is separate from your Windows password.\n\n" +
            "3. On your phone or other device, add " + address + " in Moonlight. If the PC name does not resolve, use this PC's LAN IP with the same port. Select the host to display a PIN.\n\n" +
            "4. In Sunshine on this PC, open the PIN tab and enter that PIN and a device name. Return to Moonlight and select Desktop. Keep ChildStream attached; minimize to tray while playing.").Replace("\n", Environment.NewLine);
    }
}

sealed class HostPromptPolicy
{
    uint child;
    bool asked;
    public bool ShouldAsk(uint session, HostState state, bool connected)
    {
        if (session != child) { child = session; asked = false; }
        if (state == HostState.Running) asked = false;
        if (!connected || session == 0 || state != HostState.Stopped || asked) return false;
        asked = true;
        return true;
    }
}

sealed class SessionRequestGate
{
    uint child;
    bool connected;
    bool closed;
    public int Generation { get; private set; }
    public void Update(uint session, bool attached)
    {
        if (child != session || connected != attached) { child = session; connected = attached; Generation++; }
    }
    public bool Accepts(int generation, uint session) { return !closed && connected && session == child && generation == Generation; }
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

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

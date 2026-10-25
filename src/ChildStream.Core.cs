using System;
using System.Globalization;

sealed class DisplaySettings
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Scale { get; private set; }

    DisplaySettings(int width, int height, int scale)
    {
        Width = width;
        Height = height;
        Scale = scale;
    }

    public static DisplaySettings Parse(string text)
    {
        DisplaySettings defaults = new DisplaySettings(1920, 1080, 0);
        if (text == null) return defaults;
        string[] parts = text.Trim().ToLowerInvariant().Split('x');
        int width, height, scale = 0;
        if ((parts.Length != 2 && parts.Length != 3) ||
            !int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out width) ||
            !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out height) ||
            width < 200 || width > 8192 || height < 200 || height > 8192 ||
            (parts.Length == 3 &&
                (!int.TryParse(parts[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out scale) ||
                 (scale != 0 && (scale < 100 || scale > 500)))))
            return defaults;
        return new DisplaySettings(width, height, scale);
    }
}

sealed class ChildSessionController
{
    readonly Func<uint> getChildSession;
    readonly Action<uint> logoff;

    public ChildSessionController(Func<uint> getChildSession, Action<uint> logoff)
    {
        this.getChildSession = getChildSession;
        this.logoff = logoff;
    }

    public void EndSession(uint currentSession, uint consoleSession)
    {
        EndConfirmedSession(getChildSession(), currentSession, consoleSession);
    }

    public void EndConfirmedSession(uint approvedChild, uint currentSession, uint consoleSession)
    {
        uint child = getChildSession();
        if (child != approvedChild)
            throw new InvalidOperationException("The child desktop changed after confirmation. Review it before signing out.");
        // Never let the no-child sentinel mean WTS_CURRENT_SESSION to logoff.
        if (child == uint.MaxValue || child == 0 || child == currentSession || child == consoleSession)
            throw new InvalidOperationException("No child session belonging to this launcher is available to sign out.");
        logoff(child);
    }
}

sealed class RdpScaleSettings
{
    readonly Func<string, object> getProperty;
    readonly Action<string, object> setProperty;
    object originalDesktopScale;
    object originalDeviceScale;
    bool captured;
    bool overridden;

    public RdpScaleSettings(Func<string, object> getProperty, Action<string, object> setProperty)
    {
        this.getProperty = getProperty;
        this.setProperty = setProperty;
    }

    public void Apply(int scale)
    {
        if (scale > 0)
        {
            if (!captured)
            {
                originalDesktopScale = getProperty("DesktopScaleFactor");
                originalDeviceScale = getProperty("DeviceScaleFactor");
                captured = true;
            }
            // A COM setter can fail after the other property has already changed.
            overridden = true;
            setProperty("DesktopScaleFactor", (uint)scale);
            setProperty("DeviceScaleFactor", (uint)180);
        }
        else if (overridden)
        {
            setProperty("DesktopScaleFactor", originalDesktopScale);
            setProperty("DeviceScaleFactor", originalDeviceScale);
            overridden = false;
        }
    }
}

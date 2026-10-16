using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// Compiled with the production sources; no UI, logoff, or machine changes.
static class RegressionTests
{
    static int failures;
    static int checks;

    static void Check(string name, Action test)
    {
        checks++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static Type RequiredType(string name)
    {
        Type type = typeof(Program).Assembly.GetType(name);
        Assert(type != null, name + " is missing");
        return type;
    }

    static void Settings(string text, int width, int height, int scale)
    {
        Type type = RequiredType("DisplaySettings");
        object value = type.GetMethod("Parse").Invoke(null, new object[] { text });
        Assert((int)type.GetProperty("Width").GetValue(value, null) == width, "wrong width");
        Assert((int)type.GetProperty("Height").GetValue(value, null) == height, "wrong height");
        Assert((int)type.GetProperty("Scale").GetValue(value, null) == scale, "wrong scale");
    }

    static void EndSession(uint child, uint current, uint console, bool shouldLogoff)
    {
        uint? target = null;
        Type type = RequiredType("ChildSessionController");
        object controller = Activator.CreateInstance(type, new object[] {
            new Func<uint>(delegate { return child; }),
            new Action<uint>(delegate(uint id) { target = id; })
        });
        bool refused = false;
        try { type.GetMethod("EndSession").Invoke(controller, new object[] { current, console }); }
        catch (TargetInvocationException ex)
        {
            if (!(ex.InnerException is InvalidOperationException)) throw;
            refused = true;
        }
        Assert(shouldLogoff ? target == child && !refused : target == null && refused,
            "unsafe or incorrect logoff target");
    }

    [STAThread]
    public static int Main()
    {
        Check("default display", delegate { Settings(null, 1920, 1080, 0); });
        Check("custom resolution", delegate { Settings("2800x1272", 2800, 1272, 0); });
        Check("custom scale", delegate { Settings("2800x1272x225", 2800, 1272, 225); });
        Check("whitespace and uppercase separator", delegate { Settings(" 2560 X 1440 x 150\r\n", 2560, 1440, 150); });
        Check("minimum resolution", delegate { Settings("200x200x100", 200, 200, 100); });
        Check("maximum resolution and scale", delegate { Settings("8192x8192x500", 8192, 8192, 500); });
        string[] invalid = { "", "2800", "2800xinvalid", "2800x1272xinvalid", "0x1080", "-1x1080", "8193x1080", "1920x199", "1920x1080x99", "1920x1080x501", "1920x1080x150x200", "999999999999x1080" };
        foreach (string input in invalid)
        {
            string fixture = input;
            Check("invalid display falls back atomically: " + fixture, delegate { Settings(fixture, 1920, 1080, 0); });
        }
        Check("sign out only the child returned by Windows", delegate { EndSession(7, 1, 1, true); });
        Check("refuse missing child sentinel", delegate { EndSession(uint.MaxValue, 1, 1, false); });
        Check("refuse session zero", delegate { EndSession(0, 1, 1, false); });
        Check("refuse own session", delegate { EndSession(3, 3, 1, false); });
        Check("refuse console session", delegate { EndSession(1, 3, 1, false); });
        Check("no console during switching still permits actual child", delegate { EndSession(7, 1, uint.MaxValue, true); });
        Check("failed session lookup never calls logoff", delegate {
            bool called = false;
            Type type = RequiredType("ChildSessionController");
            object controller = Activator.CreateInstance(type, new object[] {
                new Func<uint>(delegate { throw new IOException("lookup failed"); }),
                new Action<uint>(delegate { called = true; })
            });
            try { type.GetMethod("EndSession").Invoke(controller, new object[] { (uint)1, (uint)1 }); }
            catch (TargetInvocationException ex) { Assert(ex.InnerException is IOException, "lookup error was lost"); }
            Assert(!called, "logoff called after failed lookup");
        });
        Check("password reset reports failure when file cannot be deleted", delegate {
            string directory = Path.Combine(Path.GetTempPath(), "childstream-credential-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "cred.bin");
            FieldInfo credentialPath = typeof(Program).GetField("credPath", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo logPath = typeof(Program).GetField("logPath", BindingFlags.NonPublic | BindingFlags.Static);
            object previousPath = credentialPath.GetValue(null);
            object previousLog = logPath.GetValue(null);
            try
            {
                credentialPath.SetValue(null, path);
                logPath.SetValue(null, Path.Combine(directory, "launcher.log"));
                File.WriteAllText(path, "test credential fixture");
                File.SetAttributes(path, FileAttributes.ReadOnly);
                MethodInfo main = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
                int failedExit = (int)main.Invoke(null, new object[] { new string[] { "-resetpass" } });
                Assert(failedExit != 0 && File.Exists(path), "reset reported success but retained the password");
                File.SetAttributes(path, FileAttributes.Normal);
                int successExit = (int)main.Invoke(null, new object[] { new string[] { "-resetpass" } });
                Assert(successExit == 0 && !File.Exists(path), "reset did not delete the password");
            }
            finally
            {
                credentialPath.SetValue(null, previousPath);
                logPath.SetValue(null, previousLog);
                if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(directory, true);
            }
        });
        Check("clearing custom scale restores original RDP settings on reconnect", delegate {
            var properties = new Dictionary<string, object> {
                { "DesktopScaleFactor", (uint)150 }, { "DeviceScaleFactor", (uint)140 }
            };
            Type type = RequiredType("RdpScaleSettings");
            object settings = Activator.CreateInstance(type, new object[] {
                new Func<string, object>(delegate(string name) { return properties[name]; }),
                new Action<string, object>(delegate(string name, object value) { properties[name] = value; })
            });
            MethodInfo apply = type.GetMethod("Apply");
            apply.Invoke(settings, new object[] { 225 });
            Assert((uint)properties["DesktopScaleFactor"] == 225, "custom scale not applied");
            apply.Invoke(settings, new object[] { 250 });
            apply.Invoke(settings, new object[] { 0 });
            Assert((uint)properties["DesktopScaleFactor"] == 150 && (uint)properties["DeviceScaleFactor"] == 140,
                "previous scale override leaked into the next connection");
        });
        Check("partly failed scale override can be restored", delegate {
            var properties = new Dictionary<string, object> {
                { "DesktopScaleFactor", (uint)100 }, { "DeviceScaleFactor", (uint)100 }
            };
            bool failOnce = true;
            Type type = RequiredType("RdpScaleSettings");
            object settings = Activator.CreateInstance(type, new object[] {
                new Func<string, object>(delegate(string name) { return properties[name]; }),
                new Action<string, object>(delegate(string name, object value) {
                    if (name == "DeviceScaleFactor" && failOnce) { failOnce = false; throw new InvalidOperationException("COM failure"); }
                    properties[name] = value;
                })
            });
            MethodInfo apply = type.GetMethod("Apply");
            try { apply.Invoke(settings, new object[] { 225 }); }
            catch (TargetInvocationException) { }
            apply.Invoke(settings, new object[] { 0 });
            Assert((uint)properties["DesktopScaleFactor"] == 100 && (uint)properties["DeviceScaleFactor"] == 100,
                "partly applied override was not restored");
        });
        DesktopSetupTests.Run(Check);
        HostDiscoveryTests.Run(Check);
        StreamingHostTests.Run(Check);
        SessionAgentTests.Run(Check);
        LauncherTests.Run(Check);
        Console.WriteLine("C#: " + (checks - failures) + "/" + checks + " passed");
        return failures == 0 ? 0 : 1;
    }
}

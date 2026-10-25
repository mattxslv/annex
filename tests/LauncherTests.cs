using System;
using System.Windows.Forms;

static class LauncherTests
{
    static Button FindButton(Control parent, string name)
    {
        foreach (Control control in parent.Controls) {
            var button = control as Button;
            if (button != null && button.AccessibleName == name) return button;
            var found = FindButton(control, name); if (found != null) return found;
        }
        return null;
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static TextBox FindField(Control parent, string name)
    {
        foreach (Control control in parent.Controls) {
            var field = control as TextBox;
            if (field != null && field.AccessibleName == name) return field;
            var found = FindField(control, name); if (found != null) return found;
        }
        return null;
    }
    static bool HasText(Control parent, string text)
    {
        if (parent.Text == text) return true;
        foreach (Control control in parent.Controls) if (HasText(control, text)) return true;
        return false;
    }
    public static void Run(Action<string, Action> check)
    {
        check("disconnected polling preserves local discovery until the session changes", delegate {
            foreach (HostState state in new[] { HostState.Missing, HostState.Unknown, HostState.Waiting }) {
                var discovery = new HostSnapshot(state, "local discovery result", 0);
                Assert(Object.ReferenceEquals(discovery, LauncherPolicy.ForSession(discovery, 0, 0)), "poll erased local discovery");
            }
            Assert(LauncherPolicy.ForSession(new HostSnapshot(HostState.Running, "old child", 48989), 7, 0).State == HostState.Waiting, "disconnect kept stale child state");
        });
        check("preconnect discovery distinguishes failed scan and clean host absence", delegate {
            var failed = new HostDiscoveryResult { Installations = new HostInstallation[0], Diagnostics = new[] { "inspection denied" } };
            Assert(LauncherPolicy.DiscoverySnapshot(failed, false).State == HostState.Unknown, "failed scan appears absent");
            failed.Diagnostics = new string[0]; Assert(LauncherPolicy.DiscoverySnapshot(failed, false).State == HostState.Missing, "clean empty scan not reported");
        });
        check("web UI feedback names the host generically across provider selections", delegate {
            foreach (bool copied in new[] { true, false }) { string text = LauncherPolicy.SettingsFeedback(copied); Assert(text.Contains("Host web UI") && !text.Contains("Sunshine"), "provider-specific feedback mislabeled another host"); }
        });
        check("no host and discovery errors leave a configured desktop available", delegate {
            using (var form = new LauncherWindow(false)) {
                form.SetSession("Ready", "", false, true);
                form.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Missing, "No streaming host detected.", 0), Diagnostic = "One registry view was inaccessible." });
                Assert(FindButton(form, "Start desktop").Enabled && HasText(form, "No host detected"), "host absence blocked desktop");
                Assert(!FindButton(form, "Start host").Enabled, "missing selection can start");
                form.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Unavailable, "Upgrade helper", 0) });
                Assert(HasText(form, "Helper unavailable") && !HasText(form, "No host detected"), "missing helper mislabeled missing host");
            }
        });
        check("selected host changes native action names and stale actions are disabled", delegate {
            using (var form = new LauncherWindow(false)) {
                form.SetSession("Connected", "", true, true); string id = new string('a', 64);
                form.SetDiscovery(new AgentReply { SelectionId = id, Hosts = new[] { new AgentHost { Id = id, Name = "Apollo", Path = @"C:\long folder 日本語\sunshine.exe", Version = "0.4" } }, Status = new HostSnapshot(HostState.Stopped, "off", 48989) });
                Assert(FindButton(form, "Start Apollo").Enabled && HasText(form, "Apollo is off"), "selection not represented");
                Assert(FindButton(form, "Open app").Enabled, "child app launcher unavailable");
                form.SetSession("Disconnected", "", false, true); Assert(!FindButton(form, "Start Apollo").Enabled && !FindButton(form, "Open app").Enabled, "stale child actions enabled");
            }
        });
        check("new host selection prompts once while a repeated status does not", delegate {
            var policy = new HostPromptPolicy(); Assert(policy.ShouldAsk(7, "first", HostState.Stopped, true), "first host not prompted");
            Assert(!policy.ShouldAsk(7, "first", HostState.Stopped, true), "decline repeated"); Assert(policy.ShouldAsk(7, "second", HostState.Stopped, true), "new host inherited decline");
        });
        check("selected host primary action stays visible at the minimum window size", delegate {
            using (var form = new LauncherWindow(false)) {
                string first = new string('a', 64); form.SetSession("Connected", "The child desktop is attached. You can keep using your physical desktop.", true, true);
                form.SetDiscovery(new AgentReply { SelectionId = first, Hosts = new[] { new AgentHost { Id = first, Name = "Apollo", Path = @"C:\A long installed host folder\sunshine.exe", Version = "0.4" }, new AgentHost { Id = new string('b', 64), Name = "Sunshine", Path = @"C:\Other host\sunshine.exe", Version = "2026.1" } }, Status = new HostSnapshot(HostState.Stopped, "The child host is off. Start it to make this desktop available to your client.", 48989) });
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000); form.Size = form.MinimumSize; form.Show(); Application.DoEvents();
                var start = FindButton(form, "Start Apollo"); Assert(form.ClientRectangle.Contains(form.RectangleToClient(start.RectangleToScreen(start.ClientRectangle))), "primary host action falls below the scroll fold"); form.Close();
            }
        });
        check("RDP input routing is set before every connection and failures are reported", delegate {
            var fake = new InputClient(); string diagnostic = null;
            RdpInputSettings.Connect(fake, text => diagnostic = text); RdpInputSettings.Connect(fake, text => diagnostic = text);
            Assert(fake.Connections == 2 && fake.SecuredSettings2.KeyboardHookMode == 0 && diagnostic == null, "remote routing not applied before reconnect: " + diagnostic);
            RdpInputSettings.Connect(new UnsupportedInputClient(), text => diagnostic = text); Assert(diagnostic != null && diagnostic.Length <= 512, "unsupported input property hidden");
        });
        check("stopped host asks once and decline does not repeat", delegate {
            var prompt = new HostPromptPolicy();
            Assert(prompt.ShouldAsk(7, HostState.Stopped, true), "initial stopped prompt missing");
            for (int i = 0; i < 10; i++) Assert(!prompt.ShouldAsk(7, HostState.Stopped, true), "timer repeated prompt");
        });
        check("prompt waits for a connected viewer", delegate {
            var prompt = new HostPromptPolicy();
            Assert(!prompt.ShouldAsk(7, HostState.Stopped, false), "prompt before viewer attached");
            Assert(prompt.ShouldAsk(7, HostState.Stopped, true), "connection did not permit prompt");
        });
        check("new stop after running permits a new prompt", delegate {
            var prompt = new HostPromptPolicy();
            prompt.ShouldAsk(7, HostState.Stopped, true);
            Assert(!prompt.ShouldAsk(7, HostState.Running, true), "running host prompted");
            Assert(prompt.ShouldAsk(7, HostState.Stopped, true), "new host stop ignored");
        });
        check("different child gets an independent stopped prompt", delegate {
            var prompt = new HostPromptPolicy(); prompt.ShouldAsk(7, HostState.Stopped, true);
            Assert(prompt.ShouldAsk(8, HostState.Stopped, true), "new session inherited old decline");
        });
        check("uncertain and missing hosts never offer start", delegate {
            foreach (HostState state in new[] { HostState.Waiting, HostState.Checking, HostState.Unknown, HostState.Unavailable, HostState.Missing, HostState.Starting, HostState.Running }) {
                Assert(!LauncherPolicy.CanStartHost(new HostSnapshot(state, "", 48989), true), "unsafe start offered for " + state);
                Assert(!new HostPromptPolicy().ShouldAsk(7, state, true), "unsafe automatic prompt for " + state);
            }
        });
        check("start requires connection and a validated configuration port", delegate {
            Assert(LauncherPolicy.CanStartHost(new HostSnapshot(HostState.Stopped, "", 48989), true), "stopped host cannot start");
            Assert(LauncherPolicy.CanStartHost(new HostSnapshot(HostState.Error, "", 48989), true), "retry disabled");
            Assert(!LauncherPolicy.CanStartHost(new HostSnapshot(HostState.Stopped, "", 48989), false), "start without viewer");
            Assert(!LauncherPolicy.CanStartHost(new HostSnapshot(HostState.Error, "", 0), true), "invalid config can start");
        });
        check("settings URL is only validated loopback for a running host", delegate {
            Assert(LauncherPolicy.SettingsUrl(new HostSnapshot(HostState.Running, "", 48989)) == "https://127.0.0.1:48990/", "wrong web UI URL");
            Assert(LauncherPolicy.SettingsUrl(new HostSnapshot(HostState.Stopped, "", 48989)) == null, "stopped host web UI offered");
            Assert(LauncherPolicy.SettingsUrl(new HostSnapshot(HostState.Running, "", 65535)) == null, "invalid web UI URL offered");
        });
        check("child change rejects old asynchronous responses", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true); int token = gate.Generation;
            Assert(gate.Accepts(token, 7), "current response rejected");
            gate.Update(8, true); Assert(!gate.Accepts(token, 7), "old child response accepted");
        });
        check("disconnect and reconnect reject even a reused session response", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true); int token = gate.Generation;
            gate.Update(7, false); gate.Update(7, true);
            Assert(!gate.Accepts(token, 7), "stale response survived disconnect");
        });
        check("closed window rejects pending work", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true); int token = gate.Generation; gate.Close();
            Assert(!gate.Accepts(token, 7), "closed window accepted response");
        });
        check("confirmation expires after disconnect even when the child ID is reused", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true);
            var confirmation = new SessionConfirmation(gate, 7);
            Assert(confirmation.CanProceed(7), "unchanged connected child refused");
            gate.Update(0, false); gate.Update(7, true);
            Assert(!confirmation.CanProceed(7), "old confirmation authorized a new connection generation");
        });
        check("confirmation rejects replacement children and closing windows", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true);
            var confirmation = new SessionConfirmation(gate, 7);
            gate.Update(8, true);
            Assert(!confirmation.CanProceed(8), "confirmation followed replacement child");
            var next = new SessionConfirmation(gate, 8); gate.Close();
            Assert(!next.CanProceed(8), "closed window retained approval");
        });
        check("confirmed sign-out refuses a replacement at the native boundary", delegate {
            uint actual = 8, target = 0;
            var controller = new ChildSessionController(() => actual, id => target = id);
            bool refused = false;
            try { controller.EndConfirmedSession(7, 1, 1); }
            catch (InvalidOperationException) { refused = true; }
            Assert(refused && target == 0, "a replacement child was signed out");
            actual = 7; controller.EndConfirmedSession(7, 1, 1);
            Assert(target == 7, "approved unchanged child was not signed out");
        });
        check("new child shows checking while routine host polls preserve their snapshot", delegate {
            var previous = new HostSnapshot(HostState.Running, "running", 48989);
            var checking = LauncherPolicy.ForSession(previous, 7, 8);
            Assert(checking.State == HostState.Checking && checking.BasePort == 0, "new child inherited old host status");
            Assert(Object.ReferenceEquals(previous, LauncherPolicy.ForSession(previous, 7, 7)), "routine check flickers");
            Assert(LauncherPolicy.ForSession(previous, 7, 0).State == HostState.Waiting, "disconnect retains host status");
        });
        check("native window distinguishes checking from waiting", delegate {
            using (var form = new LauncherWindow(true)) {
                form.SetHost(new HostSnapshot(HostState.Checking, "Checking the child desktop's host.", 0));
                Assert(HasText(form, "Checking Sunshine"), "checking is labelled waiting");
            }
        });
        check("native controls revoke host actions on disconnect", delegate {
            using (var form = new LauncherWindow(true)) {
                form.SetSession("Connected", "test", true, true);
                form.SetHost(new HostSnapshot(HostState.Running, "running", 48989));
                Button settings = FindButton(form, "Open Sunshine settings");
                Assert(settings != null && settings.Enabled, "running settings action missing");
                form.SetSession("Disconnected", "test", false, true);
                Assert(!settings.Enabled, "disconnected window still offers host settings");
                form.SetHost(new HostSnapshot(HostState.Stopped, "off", 48989));
                Assert(!FindButton(form, "Start Sunshine").Enabled, "disconnected window offers host start");
            }
        });
        check("pairing guide distinguishes host browser from client PIN", delegate {
            string guide = LauncherPolicy.PairingInstructions("gaming-pc", new HostSnapshot(HostState.Running, "running", 50000));
            Assert(guide.Contains("normal desktop") && guide.Contains("https://127.0.0.1:50001/"), "host browser instructions missing");
            Assert(guide.Contains("gaming-pc:50000") && guide.Contains("PIN") && guide.Contains("Moonlight"), "client pairing steps missing");
        });
        check("pairing guide never invents an address for invalid settings", delegate {
            string guide = LauncherPolicy.PairingInstructions("gaming-pc", new HostSnapshot(HostState.Missing, "missing", 0));
            Assert(!guide.Contains("https://") && !guide.Contains("gaming-pc:0"), "invalid address was offered");
            Assert(guide.Contains("Start") && guide.Contains("Sunshine"), "recovery instructions missing");
        });
        check("native window exposes the settings URL without needing a browser launch", delegate {
            using (var form = new LauncherWindow(true)) {
                form.SetSession("Connected", "test", true, true);
                form.SetHost(new HostSnapshot(HostState.Running, "running", 50000));
                var field = FindField(form, "Sunshine web UI URL");
                Assert(field != null && field.ReadOnly && field.Text == "https://127.0.0.1:50001/", "manual browser fallback missing");
                Assert(FindButton(form, "Copy settings URL").Enabled, "settings URL cannot be copied");
                form.SetSession("Disconnected", "test", false, true);
                Assert(!FindButton(form, "Copy settings URL").Enabled, "stale child URL is offered");
            }
        });
        check("unavailable host disables both address copy actions", delegate {
            using (var form = new LauncherWindow(true)) {
                form.SetHost(new HostSnapshot(HostState.Unavailable, "missing helper", 0));
                Assert(!FindButton(form, "Copy address").Enabled, "unknown pairing address looks copyable");
                var copy = FindButton(form, "Copy settings URL");
                Assert(copy != null && !copy.Enabled, "unknown settings URL looks copyable");
            }
        });
        check("native desktop action exposes its current accessible name", delegate {
            using (var form = new LauncherWindow(true)) {
                form.SetSession("Connected", "test", true, true);
                Assert(FindButton(form, "Show desktop") != null, "screen reader still calls the connected action Start desktop");
            }
        });
        check("expanded pairing instructions retain a readable scrollable height", delegate {
            using (var form = LauncherPreview.Create(HostState.Running)) {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000);
                form.Show(); Application.DoEvents();
                FindButton(form, "Show pairing steps").PerformClick(); Application.DoEvents();
                var guide = FindField(form, "Pairing instructions");
                Assert(guide.Visible && guide.ClientSize.Height >= 140 && guide.ScrollBars == ScrollBars.Vertical, "guide collapsed or cannot be scrolled");
                Assert(guide.Height <= 190, "expanded guide exceeds its height budget");
                form.ClientSize = new System.Drawing.Size(900, 640); Application.DoEvents();
                Assert(guide.ClientSize.Height >= 140, "compact window collapsed its instructions");
                Assert(guide.Height <= 190, "compact guide exceeds its height budget");
                int workspaceHeight = form.Controls.Find("DesktopWorkspace", true)[0].ClientSize.Height;
                Assert(workspaceHeight >= 140, "compact guide consumed the desktop workspace: viewer=" + workspaceHeight + ", guide=" + guide.Height + ", pairing=" + guide.Parent.Height + ", clientRow=" + guide.Parent.Controls[0].Height + ", settingsRow=" + guide.Parent.Controls[1].Height + ", right=" + guide.Parent.Parent.Height);
                form.Size = form.MinimumSize; Application.DoEvents();
                var copy = FindButton(form, "Copy settings URL");
                var copyBounds = form.RectangleToClient(copy.RectangleToScreen(copy.ClientRectangle));
                Assert(form.ClientRectangle.Contains(copyBounds), "settings copy action clips at the actual minimum window: button=" + copyBounds + ", client=" + form.ClientRectangle);
                form.Close();
            }
        });
        check("native pairing steps retain separate Windows paragraphs", delegate {
            using (var form = LauncherPreview.Create(HostState.Running)) {
                string text = FindField(form, "Pairing instructions").Text;
                Assert(text.Contains("\r\n\r\n2.") && text.Contains("\r\n\r\n3.") && text.Contains("\r\n\r\n4."), "native text box merges the numbered steps");
            }
        });
    }
    public sealed class InputSettings { public int KeyboardHookMode { get; set; } }
    public sealed class InputClient {
        public InputSettings SecuredSettings2 { get; private set; } public int Connections { get; private set; }
        public InputClient() { SecuredSettings2 = new InputSettings(); }
        public void Connect() { if (SecuredSettings2.KeyboardHookMode != 1) throw new Exception("Connect before keyboard routing"); Connections++; SecuredSettings2.KeyboardHookMode = 0; }
    }
    public sealed class UnsupportedInputClient { public void Connect() { } }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

sealed class AgentRequest
{
    public int Version = 2;
    public string Command = "", SelectionId = "", AppId = "", ExecutablePath = "";
}
sealed class AgentHost { public string Id = "", Name = "", Path = "", Version = ""; }
sealed class AgentApp { public string Id = "", Name = "", Path = ""; }
sealed class AgentReply
{
    public int Version = 2;
    public string SelectionId = "", Diagnostic = "";
    public HostSnapshot Status = new HostSnapshot(HostState.Checking, "Checking the host...", 0);
    public AgentHost[] Hosts = new AgentHost[0]; public AgentApp[] Apps = new AgentApp[0];
}

static class AgentCodec
{
    static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 8 }; }
    static IDictionary<string, object> Object(object value, params string[] allowed) {
        var data = value as IDictionary<string, object>;
        if (data == null || data.Keys.Any(k => !allowed.Contains(k))) throw new InvalidDataException("Unknown or malformed helper fields.");
        return data;
    }
    static IDictionary<string, object> Parse(string text, params string[] fields) {
        try { if (text == null || Encoding.UTF8.GetByteCount(text) > 4096) throw new InvalidDataException("Helper message is too large."); return Object(Serializer().DeserializeObject(text), fields); }
        catch (ArgumentException ex) { throw new InvalidDataException("Helper upgrade/repair required: expected protocol version 2.", ex); }
        catch (InvalidOperationException ex) { throw new InvalidDataException("Malformed helper message.", ex); }
    }
    static string String(IDictionary<string, object> data, string key, int limit) {
        object value; if (!data.TryGetValue(key, out value)) return "";
        var text = value as string; if (text == null || text.Length > limit || text.IndexOf('\0') >= 0) throw new InvalidDataException("Invalid helper field: " + key); return text;
    }
    static int Number(IDictionary<string, object> data, string key) { object value; if (!data.TryGetValue(key, out value) || !(value is int)) throw new InvalidDataException("Invalid helper number: " + key); return (int)value; }
    internal static bool IsId(string id) { return id != null && (id == "" || (id.Length == 64 && id.All(c => (c >= 'a' && c <= 'f') || (c >= '0' && c <= '9')))); }
    static void Validate(AgentRequest request) {
        if (request == null || request.Version != 2) throw new InvalidDataException("Helper upgrade/repair required: expected protocol version 2.");
        if (!new[] { "HELLO", "STATUS", "SELECT_HOST", "START_HOST", "LIST_APPS", "REGISTER_APP", "LAUNCH_APP" }.Contains(request.Command)) throw new InvalidDataException("Unknown helper command.");
        if (!IsId(request.SelectionId) || !IsId(request.AppId) || request.ExecutablePath == null || request.ExecutablePath.Length > 2048 || request.ExecutablePath.IndexOf('\0') >= 0) throw new InvalidDataException("Invalid helper payload.");
        if ((request.Command == "SELECT_HOST" || request.Command == "START_HOST") && request.SelectionId.Length == 0) throw new InvalidDataException("Host selection is required.");
        if (request.Command != "REGISTER_APP" && request.ExecutablePath.Length != 0) throw new InvalidDataException("Unexpected executable field.");
        if (request.Command != "LAUNCH_APP" && request.AppId.Length != 0) throw new InvalidDataException("Unexpected app identity.");
        if (request.Command == "LAUNCH_APP" && request.AppId.Length == 0) throw new InvalidDataException("App identity is required.");
    }
    static string Serialize(object value) { string text; try { text = Serializer().Serialize(value); } catch (InvalidOperationException ex) { throw new InvalidDataException("Helper message is too large.", ex); } if (Encoding.UTF8.GetByteCount(text) > 4096) throw new InvalidDataException("Helper message is too large."); return text; }
    public static string Encode(AgentRequest request) { Validate(request); return Serialize(request); }
    public static AgentRequest DecodeRequest(string text) {
        var data = Parse(text, "Version", "Command", "SelectionId", "AppId", "ExecutablePath");
        var request = new AgentRequest { Version = Number(data, "Version"), Command = String(data, "Command", 24), SelectionId = String(data, "SelectionId", 64), AppId = String(data, "AppId", 64), ExecutablePath = String(data, "ExecutablePath", 2048) };
        Validate(request); return request;
    }
    public static string Encode(AgentReply reply) {
        if (reply == null || reply.Version != 2 || !IsId(reply.SelectionId) || reply.Diagnostic == null || reply.Diagnostic.Length > 512 || reply.Hosts == null || reply.Hosts.Length > 16 || reply.Apps == null || reply.Apps.Length > 16) throw new InvalidDataException("Invalid helper reply.");
        string text = Serialize(new { Version = 2, reply.SelectionId, reply.Diagnostic, State = reply.Status.State.ToString(), Message = reply.Status.Message, Port = reply.Status.BasePort, reply.Hosts, reply.Apps });
        DecodeReply(text); return text;
    }
    static object[] Array(IDictionary<string, object> data, string key) { object value; if (!data.TryGetValue(key, out value)) return new object[0]; var array = value as object[]; if (array == null || array.Length > 16) throw new InvalidDataException("Invalid helper list."); return array; }
    public static AgentReply DecodeReply(string text) {
        var data = Parse(text, "Version", "SelectionId", "Diagnostic", "State", "Message", "Port", "Hosts", "Apps");
        if (Number(data, "Version") != 2) throw new InvalidDataException("Helper upgrade/repair required: expected protocol version 2.");
        string stateText = String(data, "State", 24); HostState state; int port = Number(data, "Port");
        if (!Enum.TryParse(stateText, out state) || state.ToString() != stateText || !Enum.IsDefined(typeof(HostState), state) || (port != 0 && !HostConfiguration.IsValidPort(port)) || ((state == HostState.Running || state == HostState.Stopped || state == HostState.Starting) && port == 0)) throw new InvalidDataException("Invalid helper host state.");
        var reply = new AgentReply { SelectionId = String(data, "SelectionId", 64), Diagnostic = String(data, "Diagnostic", 512), Status = new HostSnapshot(state, String(data, "Message", 512), port) };
        if (!IsId(reply.SelectionId)) throw new InvalidDataException("Invalid selected host identity.");
        reply.Hosts = Array(data, "Hosts").Select(value => { var h = Object(value, "Id", "Name", "Path", "Version"); var host = new AgentHost { Id = String(h, "Id", 64), Name = String(h, "Name", 40), Path = String(h, "Path", 1024), Version = String(h, "Version", 80) }; if (!IsId(host.Id) || host.Id.Length == 0 || !new[] { "Sunshine", "Apollo", "VibePollo" }.Contains(host.Name)) throw new InvalidDataException("Invalid discovered host."); return host; }).ToArray();
        reply.Apps = Array(data, "Apps").Select(value => { var a = Object(value, "Id", "Name", "Path"); var app = new AgentApp { Id = String(a, "Id", 64), Name = String(a, "Name", 80), Path = String(a, "Path", 1024) }; if (!IsId(app.Id) || app.Id.Length == 0) throw new InvalidDataException("Invalid app."); return app; }).ToArray();
        return reply;
    }
}

sealed class AgentService
{
    readonly string root, instance = Guid.NewGuid().ToString("N"); readonly int session;
    readonly IHostDiscoverySource source; readonly Func<string, HostProcess[]> processes; readonly Func<ChildHostProfile, HostLaunchReceipt> launch; readonly Func<bool> isChild;
    readonly object gate = new object(); string selected; ChildHostProfile profile; HostController controller;
    readonly Dictionary<string, ChildApp> apps = new Dictionary<string, ChildApp>();
    public AgentService(string root, int session, IHostDiscoverySource source, Func<string, HostProcess[]> processes, Func<ChildHostProfile, HostLaunchReceipt> launch, Func<bool> isChild) {
        this.root = root; this.session = session; this.source = source; this.processes = processes; this.launch = launch; this.isChild = isChild; selected = HostSelectionStore.Load(Path.Combine(root, "host-selection.txt"));
    }
    string Key(HostInstallation host) { return HostProfiles.Digest(instance + "|" + host.Id + "|" + (profile != null && profile.Installation.Id == host.Id ? profile.Identity : "unprepared")); }
    bool StillSelected(ChildHostProfile prepared) { return isChild() && selected == prepared.Installation.Id && prepared.CurrentIdentity() == prepared.Identity && HostDiscovery.Find(root, source).Installations.Any(h => h.Id == prepared.Installation.Id); }
    public AgentReply Handle(AgentRequest request) {
        AgentCodec.Encode(request); // Validate at the service boundary even for an in-process caller.
        lock (gate) {
            if (request.Command == "LIST_APPS" || request.Command == "REGISTER_APP" || request.Command == "LAUNCH_APP") return HandleApps(request);
            var found = HostDiscovery.Find(root, source); var choice = HostSelection.Choose(found.Installations, HostSelectionStore.Load(Path.Combine(root, "host-selection.txt")) ?? selected, null);
            string diagnostic = HostInstallation.Limit(string.Join(" ", found.Diagnostics), 512);
            // A pending elevation owns the original controller until its worker settles.
            // Neither configuration refresh nor a different saved selection may replace it.
            if (controller != null && controller.StartInProgress) return Bounded(new AgentReply {
                SelectionId = Key(profile.Installation), Hosts = Describe(found.Installations),
                Status = new HostSnapshot(HostState.Starting, "Host startup is still pending in the Annex desktop. Check its elevation prompt before changing settings or starting another host.", profile.BasePort), Diagnostic = diagnostic
            });
            if (request.Command == "SELECT_HOST") {
                HostInstallation target = found.Installations.FirstOrDefault(h => Key(h) == request.SelectionId);
                if (controller != null && controller.GetStatus().State == HostState.Starting) diagnostic = "Wait for the current host startup to finish before changing hosts.";
                else if (target == null) diagnostic = "The host selection changed. Refresh and choose it again.";
                else { choice.Selected = target; choice.RequiresChoice = false; }
            }
            HostInstallation current = choice.Selected;
            if (current == null) {
                selected = null; profile = null; controller = null;
                return Bounded(new AgentReply { Status = LauncherPolicy.DiscoverySnapshot(found, true), Hosts = Describe(found.Installations), Diagnostic = diagnostic });
            }
            selected = current.Id;
            var reply = new AgentReply { SelectionId = Key(current), Hosts = Describe(found.Installations), Diagnostic = diagnostic };
            try {
                if (profile == null || profile.Installation.Id != current.Id || profile.CurrentIdentity() != profile.Identity) {
                    profile = HostProfiles.Prepare(root, current, HostPorts.Occupied()); var prepared = profile;
                    controller = new HostController(prepared, session, () => processes(prepared.Installation.ExecutablePath), () => {
                        lock (gate) { if (!StillSelected(prepared)) throw new IOException("The Annex desktop, selected host or its configuration changed. Refresh and confirm startup again."); }
                        return launch(prepared);
                    });
                }
                reply.SelectionId = Key(current); reply.Hosts = Describe(found.Installations);
                reply.Status = controller.GetStatus();
                if (request.Command == "START_HOST") {
                    if (!isChild()) reply.Status = new HostSnapshot(HostState.Unavailable, "This helper is not in the actual Annex desktop. Reconnect or repair the helper.", 0);
                    else if (request.SelectionId != reply.SelectionId) reply.Diagnostic = "The host selection changed. Review the current host and confirm again.";
                    else reply.Status = controller.RequestStart();
                }
                if (request.Command == "SELECT_HOST") HostSelectionStore.Save(Path.Combine(root, "host-selection.txt"), current.Id);
            } catch (Exception ex) { reply.Status = new HostSnapshot(HostState.Error, ex.Message, 0); }
            return Bounded(reply);
        }
    }
    AgentHost[] Describe(HostInstallation[] hosts) { return hosts.Select(h => new AgentHost { Id = Key(h), Name = h.DisplayName, Path = HostInstallation.Limit(h.ExecutablePath, 1024), Version = h.Version }).ToArray(); }
    AgentReply HandleApps(AgentRequest request) {
        var reply = new AgentReply { Status = new HostSnapshot(HostState.Waiting, "Apps do not require a streaming host.", 0) };
        try {
            if (!isChild()) throw new IOException("Reconnect the actual Annex desktop before opening apps.");
            apps.Clear();
            var candidates = ChildAppCatalog.Find(new NativeAppDiscovery()).ToList(); string record = Path.Combine(root, "child-apps.txt"); var registered = ChildAppCatalog.LoadRegistered(record).ToList();
            foreach (string path in registered) { try { var app = ChildAppCatalog.Register(path); if (!candidates.Any(a => HostController.SamePath(a.ExecutablePath, path))) candidates.Add(app); } catch (IOException) { } catch (ArgumentException) { } }
            if (request.Command == "REGISTER_APP") { var app = ChildAppCatalog.Register(request.ExecutablePath); if (registered.Count >= 16 && !registered.Contains(app.ExecutablePath, StringComparer.OrdinalIgnoreCase)) throw new IOException("The child app list is full (16). Remove an entry from child-apps.txt before adding another."); registered.Add(app.ExecutablePath); ChildAppCatalog.SaveRegistered(record, registered); if (!candidates.Any(a => HostController.SamePath(a.ExecutablePath, app.ExecutablePath))) candidates.Add(app); }
            foreach (var app in candidates.Take(16)) apps[HostProfiles.Digest(instance + "|" + app.Id)] = app;
            reply.Apps = apps.Select(pair => new AgentApp { Id = pair.Key, Name = pair.Value.DisplayName, Path = HostInstallation.Limit(pair.Value.ExecutablePath, 1024) }).ToArray();
            if (request.Command == "LAUNCH_APP") {
                ChildApp app; if (!apps.TryGetValue(request.AppId, out app)) throw new IOException("The selected app changed or the helper restarted. Refresh and choose it again.");
                AppLaunchSpec spec = ChildAppLauncher.Build(app, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                reply.Diagnostic = ChildAppLauncher.Launch(spec, (uint)session, () => isChild() ? (uint)session : 0, ChildAppLauncher.NativeStart).Message;
            }
        } catch (Exception ex) { reply.Diagnostic = HostInstallation.Limit(ex.Message, 512); }
        return Bounded(reply);
    }
    static AgentReply Bounded(AgentReply reply) {
        for (;;) {
            try { AgentCodec.Encode(reply); return reply; }
            catch (InvalidDataException) {
                if (reply.Hosts.Length > 0) reply.Hosts = reply.Hosts.Take(reply.Hosts.Length - 1).ToArray();
                else if (reply.Apps.Length > 0) reply.Apps = reply.Apps.Take(reply.Apps.Length - 1).ToArray();
                else throw;
                reply.Diagnostic = "Some entries exceed this reply's size limit. Use Browse to choose an executable.";
            }
        }
    }
}

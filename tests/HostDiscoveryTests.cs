using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class HostDiscoveryTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Source : IHostDiscoverySource
    {
        public readonly List<HostCandidate> Items = new List<HostCandidate>();
        public readonly Dictionary<string, HostFileMetadata> Files = new Dictionary<string, HostFileMetadata>(StringComparer.OrdinalIgnoreCase);
        public string Denied; public bool Fail;
        public IEnumerable<HostCandidate> Candidates(string root) { if (Fail) throw new UnauthorizedAccessException("fixture registry denied"); return Items; }
        public bool Exists(string path) { return Files.ContainsKey(path); }
        public HostFileMetadata Inspect(string path) { if (path == Denied) throw new UnauthorizedAccessException("fixture metadata denied"); return Files[path]; }
        public string Add(string root, string folder, string product, HostKind? hint, string origin) {
            string path = Path.Combine(root, folder, "sunshine.exe");
            Items.Add(new HostCandidate(path, hint, origin)); Files[path] = new HostFileMetadata(product, product, "1.0", "first"); return path;
        }
    }
    public static void Run(Action<string, Action> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "childstream-discovery-" + Guid.NewGuid().ToString("N"));
        check("no streaming host is a valid empty discovery result", delegate {
            var result = HostDiscovery.Find(root, new Source());
            Assert(result.Installations.Length == 0 && result.Diagnostics.Length == 0, "missing host became an error");
        });
        check("discovery identifies portable and nonstandard installed Sunshine", delegate {
            var source = new Source(); source.Add(root, "portable", "Sunshine", HostKind.Sunshine, "managed"); source.Add(root, "elsewhere & λ", "Sunshine", HostKind.Sunshine, "registry");
            var result = HostDiscovery.Find(root, source);
            Assert(result.Installations.Length == 2 && result.Installations.All(h => h.Kind == HostKind.Sunshine), "valid host locations omitted");
        });
        check("fork branding and registry identity override a shared exe name", delegate {
            var source = new Source(); source.Add(root, "a", "Apollo", null, "known"); source.Add(root, "b", "Sunshine", HostKind.VibePollo, "registry");
            var result = HostDiscovery.Find(root, source);
            Assert(result.Installations.Length == 2 && result.Installations[0].Kind == HostKind.Apollo && result.Installations[1].Kind == HostKind.VibePollo, "forks collapsed into Sunshine");
        });
        check("duplicate case-varied executable paths produce one candidate", delegate {
            var source = new Source(); string path = source.Add(root, "portable", "Sunshine", HostKind.Sunshine, "managed");
            source.Items.Add(new HostCandidate(path.ToUpperInvariant(), HostKind.Sunshine, "known"));
            Assert(HostDiscovery.Find(root, source).Installations.Length == 1, "duplicate host shown");
        });
        check("uninspectable host is diagnostic while other discovery survives", delegate {
            var source = new Source(); source.Denied = source.Add(root, "denied", "Sunshine", null, "registry"); source.Add(root, "good", "Apollo", null, "known");
            var result = HostDiscovery.Find(root, source);
            Assert(result.Installations.Length == 1 && result.Installations[0].Kind == HostKind.Apollo && result.Diagnostics.Length == 1, "denied inspection hid another host or looked absent");
        });
        check("failed discovery source is distinct from no installed host", delegate {
            var result = HostDiscovery.Find(root, new Source { Fail = true });
            Assert(result.Installations.Length == 0 && result.Diagnostics.Length == 1, "discovery failure looked like a clean empty result");
        });
        check("an executable basename alone never establishes streaming-host identity", delegate {
            var source = new Source(); source.Add(root, "unknown", "Unrelated product", null, "browse");
            var result = HostDiscovery.Find(root, source);
            Assert(result.Installations.Length == 0 && result.Diagnostics.Length == 1, "unrelated sunshine.exe accepted");
        });
        check("fresh scans detect a host installed after an empty scan", delegate {
            var source = new Source(); Assert(HostDiscovery.Find(root, source).Installations.Length == 0, "initial scan not empty");
            source.Add(root, "new", "Sunshine", null, "registry");
            Assert(HostDiscovery.Find(root, source).Installations.Length == 1, "new installation ignored");
        });
        check("remembered selection survives another host appearing", delegate {
            var source = new Source(); source.Add(root, "first", "Apollo", null, "registry");
            var first = HostDiscovery.Find(root, source).Installations[0];
            source.Add(root, "managed", "Sunshine", HostKind.Sunshine, "managed");
            var choice = HostSelection.Choose(HostDiscovery.Find(root, source).Installations, first.Id, null);
            Assert(choice.Selected.Id == first.Id && !choice.RequiresChoice, "new host silently changed selection");
        });
        check("selection prefers a child instance then managed host then an explicit choice", delegate {
            var source = new Source(); source.Add(root, "a", "Apollo", null, "registry"); source.Add(root, "b", "VibePollo", null, "registry");
            var hosts = HostDiscovery.Find(root, source).Installations;
            Assert(HostSelection.Choose(hosts, null, null).RequiresChoice, "multiple installations guessed");
            Assert(HostSelection.Choose(hosts, null, hosts[1].Id).Selected.Id == hosts[1].Id, "running child selection ignored");
            source.Add(root, "c", "Sunshine", null, "managed"); hosts = HostDiscovery.Find(root, source).Installations;
            Assert(HostSelection.Choose(hosts, "deleted selection", null).Selected.Source == "managed", "managed fallback ignored");
        });
        check("replacing a selected executable changes its installation identity", delegate {
            var source = new Source(); string path = source.Add(root, "a", "Apollo", null, "registry");
            string oldId = HostDiscovery.Find(root, source).Installations[0].Id;
            source.Files[path] = new HostFileMetadata("Apollo", "Apollo", "2.0", "replacement");
            Assert(HostDiscovery.Find(root, source).Installations[0].Id != oldId, "old approval identity survived replacement");
            source.Files.Clear(); Assert(HostDiscovery.Find(root, source).Installations.Length == 0, "deleted executable remains available");
        });
        check("remembered host record rejects malformed data and retains a portable path", delegate {
            Directory.CreateDirectory(root); string record = Path.Combine(root, "selection.txt");
            try {
                var source = new Source(); string path = source.Add(root, "portable", "Sunshine", null, "browse");
                string id = HostDiscovery.Find(root, source).Installations[0].Id; HostSelectionStore.Save(record, id);
                Assert(HostSelectionStore.Load(record) == id && HostInstallation.PathFromId(id).Equals(path, StringComparison.OrdinalIgnoreCase), "portable host cannot be rediscovered");
                File.WriteAllText(record, "arbitrary command"); Assert(HostSelectionStore.Load(record) == null, "malformed selected host accepted");
            } finally { Directory.Delete(root, true); }
        });
    }
}

# Simple host and app compatibility implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user already chose direct implementation with independent final reviews.

**Goal:** Let users open the child desktop without a host, select/start an installed streaming host inside it, and launch apps there with verified browser profile isolation.

**Architecture:** Retain the WinForms/RDP viewer and local child helper. Add host discovery/providers, separate child host profiles, versioned structured IPC, and a small child app catalog/launcher. Desktop setup and helper registration live in the EXE; host installation remains optional and user-directed.

**Tech Stack:** Windows x64, .NET Framework 4.x, C# 5, WinForms/RDP ActiveX, existing PowerShell build/test tooling; built-in framework assemblies only.

**Spec:** `docs/superpowers/specs/2026-10-17-simple-host-and-app-compatibility-design.md` (approved 2026-10-17).

## Global constraints

- A missing streaming host never prevents opening the child desktop.
- Never start the console service as a substitute for the child instance.
- Declining leaves the desktop usable and exposes an explicit retry.
- Never accept arbitrary shell command text from a pipe request.
- No silent child sign-out.
- Keep the existing native Windows appearance and a small number of actions.
- Future delivery contains only the EXE unless the user asks for additional artifacts.
- No managed packages, protocol/client changes, browser sandbox disabling, driver installation, purchases or authentication automation.
- Existing child credentials, Sunshine pairings/configuration, display settings and main-desktop applications must survive routine updates.
- Same-user Windows child sessions cannot guarantee universal third-party app compatibility. Report actual verified behavior and concrete limitations.

## Review focus

1. A same-named sunshine.exe from a different fork/path must not silently replace the user's selected host; Task 2 pins identity/selection behavior.
2. A selected host can be removed/replaced or changed while confirmation/async work is pending; Tasks 2 and 4 reject stale identities and generations.
3. A manual child host may use a different configuration than the prepared profile; Tasks 3 and 4 do not advertise guessed ports or misidentify its state.
4. Writable profiles may contain malformed data, Unicode/spaces or Windows path metacharacters; Tasks 1, 3 and 5 pin safe serialization/launch behavior without shell evaluation.
5. A browser/game/app can exit, reuse a process in another session, or display authentication; Task 5 distinguishes launch dispatch/process/session observations from working UI and leaves authentication to the user.

## File map

- `src/DesktopSetup.cs`: built-in desktop setup plan/executor and installation registration; no host download.
- `src/HostDiscovery.cs`: installation descriptors, discovery sources, branding and remembered selection.
- `src/HostProfiles.cs`: provider-specific config paths/options, isolated profiles and port-family checks.
- `src/StreamingHost.cs`: selected-host controller/status, existing process/start concurrency policy.
- `src/SessionAgent.cs`: versioned framed request/reply transport and native peer/session boundary.
- `src/ChildApps.cs`: detected app catalog, safe argument quoting, browser profiles and child launches.
- `src/LauncherPolicy.cs`: desktop/host selection/prompt state policy.
- `src/LauncherWindow.cs`, `src/LauncherApplication.cs`: native actions, chooser/dialog integration, RDP setup and request sequencing.
- `src/ChildStream.cs`: CLI dispatch, setup entry and existing credential/session functions.
- `tests/*Tests.cs`: non-destructive policy/native-control/transport tests registered in `RegressionTests.cs`.
- Build/test scripts and CI: add only required framework references, retain optional packaging tooling, upload the EXE by default.

Use the existing `Action<string, Action> check` test harness and fixture style. Tests inject discovery, listener, registry, launch and native-session boundaries; never execute real setup, host startup or sign-out to prove a unit test.

## Verification commands

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
git diff --check
```

The local app-container sandbox prevents real named-pipe peers from communicating; run that existing suite outside it. Run the full suite after each task's red/green change; avoid repeating broad checks after a green run without a new change or unresolved failure. Run PowerShell 7 as well at final validation. Baseline before this plan: 92 C# + 6 PowerShell + 3 packaging cases pass.

### Task 1: EXE-contained desktop setup and installation identity

**Files:** Create `src/DesktopSetup.cs`, `tests/DesktopSetupTests.cs`; modify `src/ChildStream.cs`, `scripts/setup.ps1`, `tests/scripts.tests.ps1`, `tests/RegressionTests.cs`.

**Interfaces:**
- `DesktopSetupPlan Create(string installRoot, string startupDirectory)` describes registry changes, native child-session enablement, shortcut target/arguments and the installation record.
- `DesktopSetupResult Apply(DesktopSetupPlan plan, IDesktopSetupActions actions)` applies only those operations, returns success plus a bounded diagnostic and does not download a host.
- `IDesktopSetupActions` exposes `EnableChildSessions()`, `WriteRegistryDword(string path, string name, int value)`, `WriteStartupShortcut(string path, string executable, string arguments)` and `WriteInstallationRecord(string installRoot)`; fake actions record calls.
- `InstallationIdentity.Load()` returns a canonical registered root or no registration. Registration is written only after required setup operations succeed.

- [ ] Write failing tests: setup without any host still produces a usable desktop plan; plan targets the exact install EXE with `-agent`, has no script/download dependency, safely preserves spaces/Unicode/metacharacters; native-enable failure prevents a success/registration result; an unrelated startup entry is preserved.
- [ ] Run the suite and confirm the new tests fail for the missing setup implementation.
- [ ] Implement the plan/executor and production actions. Preserve current child-enable, RDP/minimized-rendering settings. Prefer a native startup `.lnk` pointing to the EXE over interpolated shell commands. Retire only the recognized old ChildStream hook during an explicit setup/repair operation. Report partial failure; do not claim transactional rollback.
- [ ] Add `-setup` to the EXE and a normal GUI setup/repair entry. Elevate only the explicit setup action; leave UAC interaction to the user. Keep `-agent`, existing build/check/reset commands and same-user credential storage.
- [ ] Change `scripts/setup.ps1` into the source-development wrapper for built-in setup; remove its mandatory Sunshine download. Retain existing configuration helper functions/tests where still useful, and update the setup tests to assert the new host-optional behavior.
- [ ] Run suite/build/diff check, then commit this independently testable setup change.

### Task 2: Installed-host discovery and stable selection

**Files:** Create `src/HostDiscovery.cs`, `tests/HostDiscoveryTests.cs`; modify `tests/RegressionTests.cs` and framework references in build/test scripts only if required.

**Interfaces:**
- `enum HostKind { Sunshine, Apollo, VibePollo }`.
- `HostInstallation` exposes `Id`, `Kind`, `DisplayName`, `ExecutablePath`, `Version`, `Source` and `Diagnostic`.
- `HostDiscovery.Find(string installRoot, IHostDiscoverySource source)` returns deduplicated candidates plus inaccessible/ambiguous diagnostics.
- `IHostDiscoverySource` supplies saved/managed/known-directory/uninstall/process candidates and executable metadata; production readers never scan whole drives.
- `HostSelection.Choose(HostInstallation[] candidates, string rememberedId, string runningChildId)` returns an explicit selected candidate or a choice-required state.
- `HostSelectionStore.Load/Save(string path, string installationId)` persists a bounded identifier, with malformed/missing state treated as no saved selection.

- [ ] Write failing tests for no candidates; known portable Sunshine; installed Sunshine at a nonstandard path; Apollo/VibePollo sharing the sunshine.exe filename; duplicate paths/case; inaccessible registry/metadata; ambiguous branding; deleted/replaced selection; multiple candidates; detection appearing after initial empty scan.
- [ ] Assert selection priority: valid remembered selection, verified running child identity, existing managed portable installation, otherwise explicit choice when multiple. Another installation appearing never silently changes the selected host.
- [ ] Run RED, implement discovery/selection with native readers separated from pure policy, then run GREEN.
- [ ] Validate files/metadata before declaring a candidate launchable. Browse adds an explicitly selected local executable; unsupported metadata remains an explanatory result.
- [ ] Commit the discovery and selection unit.

### Task 3: Independent child host profiles and provider launch contracts

**Files:** Create `src/HostProfiles.cs`, `tests/HostProfileTests.cs`; modify `src/StreamingHost.cs`, `tests/StreamingHostTests.cs`, `tests/RegressionTests.cs`.

**Interfaces:**
- `IHostAdapter` exposes `HostKind Kind`, `string Validate(HostInstallation installation)` (null means supported), `string BuildConfiguration(HostInstallation installation, string directory, int basePort)` and `HostLaunchSpec BuildLaunch(ChildHostProfile profile)`.
- `HostLaunchSpec` contains the exact executable, native argument vector and working directory; it never contains a shell command.
- `ChildHostProfile` exposes `Installation`, `ConfigPath`, `DataDirectory`, `BasePort`, `Arguments` and `Identity`.
- `HostProfiles.Prepare(string installRoot, HostInstallation installation, NetworkPort[] occupied)` preserves a valid existing child profile or creates a new isolated one.
- `NetworkPort` records TCP/UDP and port. `HostPorts.Family(int basePort)` supplies the verified provider port family; `FindAvailable` starts at 48989 and validates the complete derived range.
- Extend `HostProcess` with PID and inspectable command/config identity. `HostController` consumes the selected profile plus process/launch callbacks; preserve serialized starts and uncertain-inspection guards.

- [ ] Write failing tests: main/child data directories and state/cert/app/log paths differ; existing managed child config/pairings are preserved; no main configuration/credentials/prep commands are copied; selected profile is writable; malformed/denied config is explicit.
- [ ] Add port tests for each TCP/UDP member, IPv4/IPv6 listener collisions, exhausted/out-of-range candidates, preserving an already valid child port, and a bind/start failure after the preflight check. Existing listeners are never killed.
- [ ] Pin controller identity tests: wrong session/path/config does not become running; denied config identity becomes unknown; manually running child config uses its actual validated port or remains explicitly unknown; a console host never satisfies child status.
- [ ] Run RED. Implement provider contracts from current primary docs/source. Record tested versions and reject unsupported options/builds explicitly. Verify fork-specific virtual-display behavior before setting child capture options; do not install drivers or alter the console service.
- [ ] Use independent profile files and native process invocation. Add framework-native process command-line inspection only where required to distinguish configurations; never log credentials or unrelated command lines.
- [ ] Run GREEN/build and commit profiles/providers/controller changes. Actual fork stream/coexistence tests remain distinct acceptance evidence.

### Task 4: Versioned helper transport and stale-selection protection

**Files:** Modify `src/SessionAgent.cs`, `src/LauncherPolicy.cs`, `tests/SessionAgentTests.cs`, `tests/LauncherTests.cs`; add `src/AgentMessages.cs` if DTO/codec size warrants it.

**Interfaces:**
- `AgentRequest` has `Version = 2`, `Command`, `SelectionId` and bounded typed payloads; supported commands are `HELLO`, `STATUS`, `SELECT_HOST`, `START_HOST`, `LIST_APPS`, `REGISTER_APP`, `LAUNCH_APP`.
- `AgentReply` has `Version = 2`, selected identity, host snapshot, discovered hosts/app descriptors as relevant and a bounded diagnostic.
- `AgentClient.Request(uint childSessionId, AgentRequest request)` returns `AgentReply`; keep frame size/time limits and exact native peer/session checks.
- Extend `SessionRequestGate` with a selection generation so replies/confirmations expire on child, connection or host-selection changes.
- A helper-version mismatch is reported as upgrade/repair required, not missing host and not a fallback to unvalidated commands.

- [ ] Write failing codec tests for Unicode/space-containing paths, invalid versions/enums/commands, unknown/oversized fields, malformed/truncated frames, stale selected IDs and unauthenticated/wrong-peer requests.
- [ ] Add boundary tests for changing host selection during confirmation; removal/replacement before launch; child ID reuse after reconnect; outdated helper; wrong installation; no host versus no helper. None starts a process or acts on the replacement target.
- [ ] Run RED. Implement structured encoding with a built-in framework serializer and explicit DTO validation; add only its framework reference to build/test compilation. Preserve current ACL/network denies and request timeout behavior.
- [ ] Integrate discovery/profile selection into the helper so it launches only in its own verified child session, using a validated provider profile. Apply the generation/identity check at the launch boundary.
- [ ] Run GREEN including the real local-pipe timeout tests; commit transport/generation work.

### Task 5: Child app catalog, profile isolation and safe launches

**Files:** Create `src/ChildApps.cs`, `tests/ChildAppTests.cs`; modify helper command handling and `tests/RegressionTests.cs`.

**Interfaces:**
- `ChildApp` exposes `Id`, `DisplayName`, `ExecutablePath`, `Kind` and native argument vector.
- `ChildAppCatalog.Find(IAppDiscoverySource source)` includes detected Chrome, Edge, Steam and Wallpaper Engine; `Register(string selectedLocalExecutable)` supports an explicitly browsed app/game.
- `ChildAppLauncher.Build(ChildApp app, string localAppData)` returns `AppLaunchSpec` with executable, working directory and quoted native arguments; browser profiles live under a persistent child-only data directory.
- `ChildAppLauncher.Launch(AppLaunchSpec spec, uint approvedChild, Func<uint> currentChild, Action<AppLaunchSpec> start)` revalidates target identity and dispatches inside the helper.
- `AppLaunchResult` distinguishes dispatch failure, early exit, known other-session ownership and an unverified running process. It does not claim rendering/authentication succeeded.

- [ ] Write failing tests: Chrome/Edge launch with dedicated persistent user-data directories; normal profiles/associations remain untouched; duplicate/conflicting profile flags cannot select the normal profile; missing executables and unsafe/invalid records are rejected.
- [ ] Pin native argument quoting with spaces, Unicode, embedded quotes and trailing backslashes. No shell command concatenation, shell script evaluation or browser sandbox-disabling flag is accepted by the registered launch path.
- [ ] Test stale child/selection, quick exit, existing browser in another session, Steam ownership in another session and denied process inspection. No test stops another app or logs in.
- [ ] Run RED; implement the catalog and helper-owned registered app IDs. Resolve browsed shortcuts only through a defined validated path; do not turn request text into arbitrary commands.
- [ ] Preserve the normal Steam session when detecting conflicts. Expose a concrete handoff message instead of killing it. Leave game/account/anti-cheat restrictions explicit. Wallpaper Engine launch/reporting uses its child process/UI and verified monitor state; no automatic global wallpaper replacement.
- [ ] Run GREEN/build and commit. Keep the earlier successful live Chrome comparison as evidence and repeat it after product integration if the launch path changes.

### Task 6: Simple native host/app flow and RDP input configuration

**Files:** Modify `src/LauncherWindow.cs`, `src/LauncherApplication.cs`, `src/LauncherPolicy.cs`, `tests/LauncherTests.cs`; add a focused `src/ChildAppWindow.cs` only for the app picker; update the existing surface brief.

**Interfaces:**
- Window receives host discovery/selected-host state separately from desktop setup/connection state.
- Expose host selection/refresh/browse/install-link events, Start selected host, Open settings, Open app and existing viewer controls.
- `HostPromptPolicy.ShouldAsk` keys on child plus selected installation; changing selection invalidates an old prompt but timer ticks do not repeat a decline.
- `ChildAppWindow` chooses a catalog item or browsed EXE and returns its registered app ID; launch is a helper request, not a parent-desktop Process.Start.
- `RdpInputSettings.Apply(dynamic ocx)` sets supported remote keyboard routing before connection and reports a failure through a bounded diagnostic/log. Keep a documented way to leave viewer focus.

- [ ] Write failing tests proving No host detected and discovery errors never disable Start desktop when Windows setup is ready; host actions require an attached child and a valid selection; host-installed-after-connect prompts once; unavailable helper is not labeled absent host.
- [ ] Add native tests for multi-host choice, long installation names/paths, disabled stale actions, dynamic accessible names, compact minimum size, and the existing pairing guide/viewer height budget. Keep unknown URLs uncopyable.
- [ ] Add fake-COM tests asserting keyboard routing is applied before Connect, reconnect preserves the setting, and unsupported properties produce a diagnostic. Keep session/credential behavior unchanged.
- [ ] Run RED. Read Impeccable craft-floor immediately before UI edits; preserve the established WinForms Operate direction and native focus/system-color behavior. Keep install choices secondary and status/pairing copy specific to the selected host.
- [ ] Wire asynchronous discovery/requests without blocking the UI, rechecking connection/selection after awaits. Open vendor install pages only on user action. The child can keep running while no host exists or a start is declined.
- [ ] Run GREEN/build; perform at most the bounded author visual passes from the design skill; commit integrated native flow.

### Task 7: Real acceptance, documentation and EXE-only delivery

**Files:** Update `README.md`, `docs/pairing.md`, `docs/compatibility.md`, `docs/code-review.md`, `AGENTS.md`, `DESIGN.md`, `.impeccable/design.json`, build/CI/source manifests as needed. Optional package tooling remains available only when requested.

- [ ] Run the full suite under Windows PowerShell 5.1 and PowerShell 7 plus production build/diff check on the final tree. Record actual new case counts and unresolved platform limits.
- [ ] Perform authorized native smoke tests: no-host desktop, late detection, selected host prompt/decline/retry, helper version/path recovery, Chrome main+child persistence/navigation, Steam UI and an existing user-selected game, Wallpaper Engine child UI/display/wallpaper mapping, viewer focus/minimize/reconnect.
- [ ] Handle credentials, UAC, certificate warnings and Steam/Chrome authentication as human handoffs. Do not declare blocked hardware/account/fork tests passed. Preserve the console host and normal desktop applications throughout.
- [ ] Obtain independent whole-branch code review and native UI finish review using the applicable skills. Fix scored material findings with meaningful regressions; perform only the requested review confirmation pass, then documentation handoff.
- [ ] Rewrite run/update instructions around the actual EXE-contained setup, host-optional flow, selected host/PIN sequence and child app launcher. Record exact tested host/app builds and separate startup, capture, streaming and controller results.
- [ ] Adjust CI's default delivered artifact to the compiled EXE. Do not call `package.ps1` to create routine user outputs. Retain package tests/tooling for explicit source/bundle requests.
- [ ] Build once for delivery; synchronize the root, dist and current installed EXE/source/scripts while preserving runtime host data/credentials/display settings. Restart only this installation's old viewer/helpers with a safe recovery path; recheck every known on-disk EXE hash and the live child helper.
- [ ] Commit/push to the existing review branch and update PR #2 with the final implementation/validation scope. Check exact-head Windows CI. Upstream merge still requires maintainer permission.
- [ ] Return only the EXE link with a concise account of changes, validation and material pending cases. Do not offer ZIP/source/checksum bundles by default.

## Self-review and handoff

The tasks cover the approved setup, host discovery/providers, independent profiles,
helper/update safety, app compatibility, input/UI, validation and delivery sections.
Every Review focus item has a corresponding owner/test above. Native operations
remain behind injected boundaries in automated tests. Task interfaces use the same
HostInstallation, ChildHostProfile, AgentRequest/Reply and app identifiers across
their consumers.

Implementation method is already supplied: execute directly in the current
review/reliability checkout, with independent final reviews. Review this plan
before implementation. No request to create another worktree is needed because
the user's existing choice to work directly on this review branch is preserved.

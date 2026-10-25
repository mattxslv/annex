# Streaming Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a native Windows launcher that shows child-session Sunshine status, asks before starting it in the correct session, and ships a tested executable.

**Architecture:** Keep the existing RDP viewer and add a compact native control surface. A same-user helper runs in each logged-on session and exposes only status/start operations over a protected local pipe; the launcher contacts only the actual child session. Host/configuration rules remain independent of WinForms and native process boundaries so they can be tested without changing the host PC.

**Tech Stack:** C# 5-compatible .NET Framework, x64 WinForms, Windows RDP ActiveX, built-in named pipes, PowerShell 5.1/7. No external managed packages.

**Spec:** `docs/superpowers/specs/2026-10-16-streaming-launcher-design.md` (approved by the user).

## Global Constraints

- Keep .NET Framework, x64 WinForms, RDP ActiveX, and the existing build scripts.
- Preserve tray/reconnect, display settings, credentials, and safe child sign-out.
- Do not count Sunshine running in a different Windows session as the child's host.
- No automatic driver installation, changes to an installed host/service, custom display driver, HDR project, or full rollback installer in this scope.
- Do not claim fork-specific features or real-device compatibility without tests.
- No real logoff, session enablement, host elevation, or firewall/registry mutations during automated tests.

## Review Focus

1. A console Sunshine process or an inaccessible process must never cause a false running/stopped result for the child (Task 1 tests).
2. Repeated clicks and a delayed UAC decision must not launch duplicate hosts or freeze the UI (Tasks 1 and 3 tests).
3. A stale response, reused/disconnected child, or closing form must not update the wrong session or start anything (Task 3 tests).
4. Pipe spoofing, wrong-session peers, malformed/oversize packets, and unresponsive peers must fail closed (Task 2 tests).
5. Upgrades with an already-running child may have no helper; the app must explain recovery instead of starting on the console (Tasks 2 and 3 tests).

## File responsibilities

- `src/StreamingHost.cs`: configuration, process snapshots, host state, serialized start policy.
- `src/SessionAgent.cs`: protected pipe transport, peer validation, native process inspection, helper/client entry points.
- `src/LauncherWindow.cs`: native control surface and display-only state rendering.
- `src/ChildStream.cs`: application bootstrap, existing RDP lifecycle, host monitoring and action wiring.
- `src/ChildStream.Core.cs`: retain existing display, scaling, and sign-out rules; add only small UI policies if needed.
- `tests/StreamingHostTests.cs`, `tests/SessionAgentTests.cs`, `tests/LauncherTests.cs`: behavioral regression cases, invoked by the existing runner.
- `scripts/childsession-autostart.ps1`, `scripts/setup.ps1`: register/run the idle helper, never start Sunshine automatically.
- `scripts/package.ps1`: deterministic portable/source bundles and checksums, excluding credentials/logs/local Sunshine.
- `PRODUCT.md`, the launcher surface brief, `DESIGN.md`: confirmed product context, UI direction, and finished native design documentation.
- `README.md`, `docs/code-review.md`, `docs/compatibility.md`, `AGENTS.md`: operation, compatibility limits, verification, build/delivery instructions.

### Task 1: Host configuration, detection, and start policy

**Interfaces:** Produce `HostState`, immutable `HostSnapshot` (`State`, `Message`, `BasePort`), `HostConfiguration.Load(string baseDirectory)`, `HostConfiguration.ParsePort(string text)`, `HostProcess` records, and `HostController.GetStatus()` / `HostController.RequestStart()`. Inject process enumeration and launch functions at the OS boundary. Production launch uses only the managed portable executable and explicit managed config, never a path supplied by IPC.

- [ ] Write tests for matching path/session, console/wrong-path processes, denied inspection, absent exe/config, malformed config, duplicate port keys, and base-port bounds 6–65514. With no explicit port, Sunshine's own default is 47989; setup continues generating 48989 for a new installation.
- [ ] Write tests that a stopped host starts once, an already-running/unknown/missing host never starts, two concurrent requests serialize, cancellation/error is reported, and a process that exits immediately does not become a durable running status.
- [ ] Run `scripts/test.ps1`; observe the new behavior checks fail before implementing it.
- [ ] Implement the contracts in `src/StreamingHost.cs`, using immutable snapshots and an explicit in-progress latch. Starting returns a starting state while the launch operation runs off the UI/pipe response thread; failures remain visible until an explicit retry.
- [ ] Run the full suite in PowerShell 5.1 and 7, and build with warnings as errors. Expected: all old and new checks pass; no host process starts during tests.
- [ ] Commit the host rules and tests.

### Task 2: Session-local agent, protocol, and startup integration

**Interfaces:** Consume Task 1's host contracts. Produce `AgentClient.Request(uint childSessionId, string command) : HostSnapshot`, `SessionAgent.Run(string baseDirectory) : int`, bounded frame read/write helpers, and a peer validator using executable path and session identity. Commands are exactly `STATUS` and `START`; neither accepts arguments or shell text.

- [ ] Write tests for exact command acceptance, unknown commands, truncated/oversize frames, invalid replies, bounded read/connect timeout, wrong executable/session peers, and user/session/install-specific pipe names.
- [ ] Add an actual local-pipe round trip using an injected handler; verify bytes/response behavior without a real session agent or native start. Ensure unresponsive/disconnected peers fail within a bounded timeout.
- [ ] Run the added tests failing, then implement protected local pipes with an ACL for the current Windows user and a network-logon deny rule. Validate native pipe peer PID/path/session; identification-level client access must not grant arbitrary impersonation.
- [ ] Add the `-agent` CLI branch before any normal viewer/credential logic and a per-session helper mutex. The helper remains idle until a validated request and rechecks process state before accepting a start.
- [ ] Replace the old automatic Sunshine start script with a hidden launch of `ChildStream.exe -agent`; preserve a single common startup hook. Update script tests to verify it passes only `-agent`, waits for no UAC, logs launch failures, and never directly starts Sunshine.
- [ ] Run both PowerShell test suites and the build. Expected: all checks pass; ordinary session logon only starts an idle helper.
- [ ] Commit helper, startup wiring, and tests.

### Task 3: Native GUI, prompt policy, and RDP integration

**Interfaces:** Consume `AgentClient` snapshots, existing `DisplaySettings`, `RdpScaleSettings`, and `ChildSessionController`. `LauncherWindow` exposes a viewer workspace and events for connect/reconnect, start host, settings, minimize, setup guidance, and sign-out. A pure prompt policy decides whether a stopped-host observation should ask once; a generation token rejects stale asynchronous responses.

- [ ] Record confirmed product context and the native Operate direction: pale slate surfaces, dark text, blue actions, Segoe UI, native chrome, text-labelled status, keyboard/DPI/high-contrast support. Extend the viewer rather than inventing a web-style dashboard.
- [ ] Write tests for the stopped prompt occurring once, decline suppressing timer prompts, a new stop after running permitting a new prompt, and a changed child invalidating old responses. Test settings/start button availability for every host/session state.
- [ ] Run those tests failing. Implement the native window and integrate it without creating a second UI message loop. Create the RDP control/connect only after the user starts the desktop; show setup guidance when prerequisites are missing.
- [ ] Poll host state off the UI thread only once the real child ID is available. Keep one request in flight; discard responses after disconnect/child change/close. Ask before `START`, report unavailable helper recovery, and open only a validated loopback settings URL.
- [ ] Preserve tray handling, safe sign-out, viewer rendering/minimization, credentials, and reconnect behavior. Add a per-user/session launcher-instance guard.
- [ ] Add a clearly labelled synthetic `-preview`/capture path that never enables sessions, reads credentials, launches a host, or mutates system settings. Capture/inspect normal and larger-DPI/resized layouts, keyboard focus, and stopped/error/running states in a bounded visual QA pass.
- [ ] Run the full test suite/build. Expected: old behavior checks remain green, new prompt/state cases pass, and preview controls are operable with no Windows session changes.
- [ ] Commit GUI, integration, and tests.

### Task 4: Review, packaging, CI, and publication

**Interfaces:** Consume the complete launcher and test suite. Produce `ChildStream.exe`, a portable ZIP, a source ZIP, SHA-256 checksums, exact rebuild instructions, and an updated upstream PR. Keep Sunshine as a setup download rather than silently redistributing/installing host forks or drivers.

- [ ] Add package exclusion checks: credentials, logs, local host installation, Git metadata, and test scratch files must never enter a release. Verify archive contents by opening the produced ZIPs and checking required files and checksums.
- [ ] Run the full suite under PowerShell 5.1 and 7, the production build, and whitespace checks. Extend Windows CI to build/test/package and retain the executable/bundles if appropriate; verify the hosted result on the final commit.
- [ ] Request independent code review against the approved spec/plan, plus a fresh UI finish review using actual native captures. Fix material findings with regression evidence; document true hardware/client test limits instead of claiming every possible edge case is tested.
- [ ] Finish the native design documentation from the built artifact. Update setup/upgrade instructions, the Moonlight/Artemis client versus Apollo/VibePollo host compatibility table, remaining limitations, and actual check commands/results.
- [ ] Build/package again if source changes. Verify final artifact checksums and ZIP contents, then commit and push to `ocr1n/childstream:review/reliability`.
- [ ] Update upstream PR #2 around the final implementation and mark it ready once validated. Do not claim upstream merge: the current account has no write permission to `mattxslv/childstream`.
- [ ] Deliver local artifact links, exact compilation commands, CI evidence, and any remaining manual streaming/UAC tests.

## Execution recommendation

Native execution is recommended: these four tasks share tightly coupled contracts,
and keeping one implementer avoids repeated context/setup costs. Use one fresh code
review and the required native UI finish review before publishing the final build.

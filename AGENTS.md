# ChildStream project instructions

## Current workspace status

The workspace was initially empty. On 2026-10-16 the source was fetched from
`https://github.com/mattxslv/childstream` at `d41693e`, then reviewed and improved
on the local `review/reliability` branch. It is a Windows x64 WinForms launcher
using the installed .NET Framework 4.x compiler, with EXE-contained setup and
optional PowerShell developer wrappers.
See `README.md` and `docs/code-review.md` for behavior and remaining limitations.

## Delivery and local installations

- The user wants the compiled `ChildStream.exe` as the default deliverable. Do not
  generate or offer ZIP/source/checksum bundles unless explicitly requested.
- Keep the working source folder and known local launcher copies on the same
  compiled version when delivering an update. The current installed runtime is
  `C:\Users\Admin\Downloads\ChildStream-portable`; the workspace root and `dist`
  are build-output locations. Preserve installed Sunshine, credentials, display
  settings and logs when updating application files.
- A launcher and its child helper must use the same installation. Do not direct
  the user to run an isolated build-output executable against another folder's
  helper or imply that copying an EXE configures a new Windows installation.

Do not infer a framework, package manager, or project commands from the project
name or from globally installed Codex skills. Inspect the actual project files
after application scaffolding or source code is added, then update this file.

## Setup and development commands

| Task | Current status |
| --- | --- |
| Package manager | Inapplicable: no external managed packages or lockfiles; do not add a package manager without need. |
| Framework | .NET Framework 4.x, C# 5-compatible sources, WinForms and RDP ActiveX; x64 Windows. |
| Dependency installation | No managed dependency install. `ChildStream.exe -setup` (or `scripts/setup.ps1`) changes Windows settings and registers the helper; it downloads no host. Do not run setup merely to validate code. |
| Environment setup | No secrets or `.env` required. Uses standard Windows `SystemRoot` and `COMPUTERNAME`; runtime credentials are user-scoped DPAPI in ignored `cred.bin`. |
| Development server | Inapplicable: native desktop app. Build, then launch `ChildStream.exe` for a separately authorized interactive smoke test. |
| Lint | No dedicated linter; `.\scripts\test.ps1` parses PowerShell, and builds treat compiler warnings as errors. `git diff --check` checks whitespace. |
| Type-check | C# compiler performs type checking during `.\scripts\build.ps1` and `.\scripts\test.ps1`. |
| Tests | `.\scripts\test.ps1` from repository root; 150 C# + 7 PowerShell + 3 packaging cases. No admin rights or host-setting changes. Compatible with Windows PowerShell 5.1 and PowerShell 7. Local pipe tests require peers outside app-container isolation. |
| Production build | `.\scripts\build.ps1` from repository root; emits ignored `ChildStream.exe`; `.\scripts\package.ps1` rebuilds and creates executable/portable/source ZIPs and SHA-256 checksums in ignored `dist`, using `Framework64\v4.0.30319\csc.exe`. No elevation needed. |
| CI | `.github/workflows/checks.yml` builds and tests on Windows under both PowerShell versions and retains only the compiled EXE for 30 days. Package tests use temporary fixtures; optional bundles are not routine deliverables. |

When project files become available:

- Identify the package manager from the manifest, package-manager declaration,
  lockfile, and project documentation. Resolve conflicting evidence before
  installing dependencies; do not create competing lockfiles.
- Document the actual dependency installation and development commands.
- Document existing lint, type-check, test, and build commands, including the
  directory in which each must run. Mark inapplicable checks explicitly.
- Record necessary environment variable names and setup steps without including
  secrets.
- Run applicable checks when validating changes and report their actual results.
  Distinguish missing checks, checks that were not run, and checks that failed.
- Preserve existing instructions when updating this file. Do not scaffold an app
  or select its technology stack solely to fill in missing commands.

## Documentation lookup

<!-- context7 -->
Use Context7 MCP to fetch current documentation whenever the user asks about a library, framework, SDK, API, CLI tool, or cloud service — even well-known ones like React, Next.js, Prisma, Express, Tailwind, Django, or Spring Boot. This includes API syntax, configuration, version migration, library-specific debugging, setup instructions, and CLI tool usage. Use even when you think you know the answer — your training data may not reflect recent changes. Prefer this over web search for library docs.

Do not use for: refactoring, writing scripts from scratch, debugging business logic, code review, or general programming concepts.

## Steps

1. Always start with `resolve-library-id` using the library name and what to look up in the library's documentation, unless the user provides an exact library ID in `/org/project` format
2. Pick the best match (ID format: `/org/project`) by: exact name match, description relevance, code snippet count, source reputation (High/Medium preferred), and benchmark score (higher is better). If results don't look right, try alternate names or queries (e.g., "next.js" not "nextjs", or rephrase the question). Use version-specific IDs when the user mentions a version
3. `query-docs` with the selected library ID and what to look up in the library's documentation (not single words), scoped to a single concept. If the question spans multiple distinct concepts (e.g. routing and auth and caching), make a separate `query-docs` call per concept with the same library ID, unless the question is about how the concepts interact — combined queries dilute ranking and return shallow results for each topic
4. Answer using the fetched docs
<!-- context7 -->

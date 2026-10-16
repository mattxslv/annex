# ChildStream project instructions

## Current workspace status

The workspace was initially empty. On 2026-10-16 the source was fetched from
`https://github.com/mattxslv/childstream` at `d41693e`, then reviewed and improved
on the local `review/reliability` branch. It is a Windows x64 WinForms launcher
using the installed .NET Framework 4.x compiler, with PowerShell setup scripts.
See `README.md` and `docs/code-review.md` for behavior and remaining limitations.

Do not infer a framework, package manager, or project commands from the project
name or from globally installed Codex skills. Inspect the actual project files
after application scaffolding or source code is added, then update this file.

## Setup and development commands

| Task | Current status |
| --- | --- |
| Package manager | Inapplicable: no external managed packages or lockfiles; do not add a package manager without need. |
| Framework | .NET Framework 4.x, C# 5-compatible sources, WinForms and RDP ActiveX; x64 Windows. |
| Dependency installation | No managed dependency install. Elevated `.\scripts\setup.ps1` downloads portable Sunshine and changes Windows settings; do not use it merely to validate code. |
| Environment setup | No secrets or `.env` required. Uses standard Windows `SystemRoot` and `COMPUTERNAME`; runtime credentials are user-scoped DPAPI in ignored `cred.bin`. |
| Development server | Inapplicable: native desktop app. Build, then launch `ChildStream.exe` for a separately authorized interactive smoke test. |
| Lint | No dedicated linter; `.\scripts\test.ps1` parses PowerShell, and builds treat compiler warnings as errors. `git diff --check` checks whitespace. |
| Type-check | C# compiler performs type checking during `.\scripts\build.ps1` and `.\scripts\test.ps1`. |
| Tests | `.\scripts\test.ps1` from repository root; 85 C# + 6 PowerShell + 3 packaging cases. No admin rights or host-setting changes. Compatible with Windows PowerShell 5.1 and PowerShell 7. |
| Production build | `.\scripts\build.ps1` from repository root; emits ignored `ChildStream.exe`; `.\scripts\package.ps1` rebuilds and creates executable/portable/source ZIPs and SHA-256 checksums in ignored `dist`, using `Framework64\v4.0.30319\csc.exe`. No elevation needed. |
| CI | `.github/workflows/checks.yml` builds and tests on Windows under both PowerShell versions, packages once and retains artifacts for 30 days. |

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

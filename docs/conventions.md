# Conventions

Where a file goes, and how it is written. Every agent prompt references this file and every card
is worked under it. A correction made here carries forward into every later session; a hand edit
to one file is forgotten by the next agent. Sections marked **fill in** are this project's own
decisions: settle them with the planner and keep them current.

## Repository layout

Keep content and code separate so each can evolve on its own: a different product can replace
`assets/` without touching `code/`.

```
project/
├── assets/          # What the shipped application loads at runtime (art, audio, fonts, data)
├── bin/             # The project's own executables and scripts (not third-party)
├── build/           # Build intermediates (compiler output, the CMake build tree); git-ignored, delete at will
├── code/
│   ├── extern/      # Third-party SOURCE compiled into our build
│   ├── lib/         # Static libraries linked by both the application and the tools
│   ├── src/         # The application, one folder per module (see Application code)
│   └── tools/       # Source for custom tools (output goes to bin/)
├── docs/
│   ├── design/      # gdd/ (intent) and tdd/ (architecture and technical specs)
│   └── reference/   # Technical reference material
├── extern/
│   └── bin/         # Prebuilt third-party binaries, SDKs, middleware
├── jobs/            # The job queue: queue/ active/ review/ done/ plans/ archive/ requests/ attachments/
├── out/             # Built packages and final results, one folder per platform; git-ignored
└── test/
    ├── unit/        # Unit tests, one file per module under test, mirroring the code/ paths
    ├── integration/ # Multi-system and end-to-end tests; they drive the real binary
    └── data/        # Test vectors, golden outputs, fixtures
```

At the root: `README.md` (what the project is, its honest status, its design in brief),
`.gitignore`, `.gitattributes`. Build intermediates go to `build/` and final results to `out/`; both
are git-ignored.

### Content vs. code

- Everything the shipped application loads at runtime lives in `assets/`, never in `code/`. Code
  references assets by path or by a generated manifest, never by embedding them.
- Numbers that define behaviour (balance, tuning, tables) live in data files under `assets/data/`
  and exist nowhere else. Generated headers and blobs are outputs, never inputs, and are not
  committed.
- Asset files are not hand-edited when a generator can produce them; the generators are tools
  under `code/tools/`, and their output goes to a git-ignored folder such as
  `assets/generated/`.
- A project that ships no content keeps `assets/` empty rather than putting data in `code/`.

### Third-party code

- Source we compile ourselves goes in `code/extern/`, one directory per dependency, pinned to a
  specific version and noted in that directory's README (origin, version, license, local changes).
- Binaries, SDKs and middleware we do not compile go in `extern/bin/`. Never mix the two: if it is
  built from source as part of our build, it belongs in `code/extern/`.

### Build and tools

- `build/` holds build intermediates only (compiler output, the CMake build tree). It is
  git-ignored and can be deleted at any time without loss. Built packages and final results go to
  the git-ignored `out/<platform>/`.
- The build definition lives in `code/`, beside what it builds (`code/src/` for the application):
  a `CMakeLists.txt` (`cmake -S code -B build`), or a .NET solution with its `Directory.*.props`.
  It is the source of truth; nothing may depend on an IDE's own project files.
- `bin/` holds the project's own executables and scripts: setup and helper scripts (committed), and
  the binaries built from `code/tools/` with their runtime support files (not committed).
  Third-party executables go in `extern/bin/`. A tool whose output the application loads at runtime
  is deployed with the application, not left in `bin/`.
- Anything shared between the application and the tools is built into a **static library** under
  `code/lib/`. If a library is used by only one side, keep it next to that side instead.
  `code/lib/` holds libraries only: module interfaces and their implementations live in
  `code/src/`, and a folder is not a library.

### Application code

- `code/src/` holds one folder per module: `Scheduler.Core` (logic), `Scheduler.Host` (scheduler-next.exe),
  `Scheduler.Tray` (scheduler-tray.exe), `Scheduler.Web`, `Installer` (ProjectToolkit.iss). Adding a module is a design decision, not a
  folder decision.
- A module folder holds that module's interfaces (pure virtual / protocol only) and its
  platform-independent implementation. No OS call, window handle, socket, clock read, file handle,
  thread or graphics context appears in an interface header.
- OS-specific code goes in `code/src/platform/<platform>/`, and code shared by more than one
  platform in `code/src/platform/common/`. Nothing outside `platform/` may assume one OS.
- Utilities shared across modules live in `code/src/systems/`. **Fill in:** which of them the core
  logic may depend on, and state the dependency edges the build enforces.
- The core (the thing everything else is debugged with) stays silent and deterministic: it does not
  log, it appends to a structured event list the application layer turns into log lines; it reads
  no clock, allocates no heap in state, and takes its randomness from a seeded generator that lives
  in the state.

### Tests

- Unit tests in `test/unit/`, one file per module under test, mirroring the `code/` paths.
- Anything that crosses a system boundary or runs the real binary goes in `test/integration/`.
- Expected outputs, vectors and fixtures live in `test/data/` and are referenced by tests, never
  inlined. Golden outputs are regenerated by an explicit command, not as a side effect of a test
  run.
- Write down what a test is meant to prove before writing it. The plan travels with the commit that
  adds the test - in the job card or the plan that asked for it - not in a folder of its own.
- Tests are never weakened, skipped or deleted to make a gate pass without the developer's
  approval.

### Docs

- Intent and design: `docs/design/`, split into `gdd/` (what the thing is and must do - for a game,
  the game design document) and `tdd/` (architecture, interfaces, technical specs).
- Technical reference material: `docs/reference/`, with an index naming what each document covers.
- A doc belongs beside the code it describes only if it must change in lockstep with it; otherwise
  it goes in `docs/`.

## Project rules

The project's own conventions. Agents follow these as literally as the rules above; every one of
them should be checkable by a tool where a tool can check it.

- **Naming.** **Fill in:** types, functions, variables, members, constants, files, prefixes.
- **Formatting.** **Fill in:** the formatter config at the root (`.clang-format`, `.editorconfig`,
  `dotnet format`, ...) and how it is run. Style is enforced by tooling, not by memory.
- **Errors.** **Fill in:** status codes or exceptions, and where each is allowed.
- **Logging.** **Fill in:** the log levels, where logging is allowed at all, and how a headless run
  answers "what happened and why".
- **Memory and ownership.** **Fill in:** who owns what, and how that is shown in the types.
- **Headers and includes.** **Fill in:** include order, forward declarations, what may include
  what.
- **License header.** New `.cs` files under `code/src/` and `test/unit/`, and `.ps1` files under `bin/`, carry these two lines first (in a `.ps1`, right after its comment-based help, with `#` for `//`); a guard test fails without them:
  `// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.`
  `// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)`
- **Comments.** **Fill in:** what a header block must say (purpose, what it does not do, its
  dependencies) and what earns an inline comment.
- **Invariants.** **Fill in:** the numbered rules the core must never break, and the test that
  catches a break. State the symptom to recognise when one breaks silently.

## Empty directories

Directories are kept in version control with a `.gitkeep` placeholder. Delete a `.gitkeep` as soon
as the directory contains a real file. `out/<platform>/` is git-ignored but must exist, so it keeps a
committed `.gitkeep`; `build/` is not kept at all.

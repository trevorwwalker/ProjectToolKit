# {{PROJECT}}

The project's own front page: what it is, what is really in the repository, and where the
authoritative documents are. Keep it honest and current - every agent reads it before it reads
anything else. Replace every **fill in** line; delete this line when you do.

# OVERVIEW

- **What it is.** **Fill in:** one paragraph. What the thing is, who uses it, and the one idea it
  turns on. Say what it is not if that is easy to get wrong.
- **Target platform and toolchain.** **Fill in:** the platform, the primary compiler/SDK and the
  versions, and which alternatives are CI targets rather than blockers.
- **Language and rules.** **Fill in:** the language standard, and the hard rules (for example: no
  exceptions in the core, status codes instead; nothing outside the platform layer may assume one
  OS).
- **Architecture.** **Fill in:** one line. The shape, in words - modules and interfaces, and what
  is authoritative.

# STATUS - what is in the repository right now

- **Fill in:** what exists, what compiles, what runs, what is only declared. Name the gap between
  the documents and the code.
- **Fill in:** what was removed, why, and the commit that removed it, so a later agent can read the
  history instead of rediscovering the mistake.
- **Fill in:** known doc drift left alone on purpose, and what will settle it.

This section is the reason an agent does not build on top of something that was never there. Do
not let it go stale.

# DESIGN

- **Authoritative documents:**
  - Intent: [docs/design/gdd/{{PROJECT}}.md](docs/design/gdd/{{PROJECT}}.md) - what it must do and
    why, the values, the rules a player or user feels.
  - Architecture: [docs/design/tdd/{{PROJECT}}-technical.md](docs/design/tdd/{{PROJECT}}-technical.md)
    - layering, interfaces, the invariants, the build and test architecture.
  - **Fill in:** each further system spec in `docs/design/tdd/`, one link per document.
- **Where documents disagree:** the intent document wins on what the thing should do; the technical
  document wins on technical parameters. Neither wins silently - an unresolved conflict is a card.
- **Fill in:** the pillars, the core loop or flow, and the rules that are load-bearing (the ones
  whose removal quietly ruins the result).

# TECHNICAL DESIGN

## System architecture

- **Fill in:** the modules under `code/src/`, one line each: what it owns, what it may depend on,
  and what must never reach into it. The module set is the design decision; the folders follow it.
- **The one rule that matters.** **Fill in:** the core module and everything it is forbidden to
  touch (a window, a socket, a clock, a file, a thread, a heap allocation in state). If a line in
  the core needs one of those, the design is wrong and the line belongs a layer up.
- **Replaceable presentation.** **Fill in:** the stub/headless configuration the agents work in
  (no window, no audio, no third-party dependency at all) and the fact that it must never break.

## Invariants

- **Fill in:** the numbered rules the core must never break, with a pointer to the document that
  states them and the test that catches a break. These are requirements, not goals.

## Naming conventions

- The project's rules are in [docs/conventions.md](docs/conventions.md). **Fill in:** the prefixes
  and cases here only if an agent must know them before it opens that file, and any deliberate
  exception, with the reason.

## File organization

The root conforms to `docs/conventions.md`; the layout below is that file, restated. `code/src/` is
the one place this file overrides it, because the module set is decided here.

        assets/          what the shipped application loads at runtime
        bin/             the project's own executables and scripts
        build/           build intermediates; git-ignored, delete at will
        code/
          extern/        third-party SOURCE compiled into our build
          lib/           static libraries shared by the application and the tools
          src/           the application: <the modules, listed>
          tools/         source for custom tools (output goes to bin/)
        docs/
          ai/            new plans, not yet carded
          design/        gdd/ (intent) and tdd/ (architecture)
          reference/     technical reference material
        extern/bin/      prebuilt third-party binaries, SDKs, middleware
        jobs/            the job queue
        out/             built packages and final results, per platform
        test/
          unit/          one file per module under test, mirroring the code/ paths
          integration/   multi-system and end-to-end; drives the real binary
          data/          vectors, golden outputs, fixtures - referenced, never inlined

- **The build definition is the source of truth.** It lives in `code/`, beside what it builds;
  intermediates land in the git-ignored `build/`, final results in the git-ignored `out/`.
- Nothing may depend on an IDE's own project files; the headless configuration builds without them.
- Empty directories are kept in version control with a `.gitkeep`; a `.gitkeep` is deleted as soon
  as the directory holds a real file.
- There is no root setup script. The toolchain is checked by the build presets, which fail loudly
  and early instead.

# TOOLING

- Custom tool source lives in `code/tools/`; the binaries land in `bin/` with whatever runtime
  support they need, beside the project's own scripts. A tool whose output the application loads at runtime is deployed with the
  application, not left in `bin/`.
- **Fill in:** each tool, one line: what it does, how to run it, and what it must never do (for
  example: never write into `code/`).
- The agent-facing tools are the ones that make the application debuggable: they replay a run, diff
  two runs, and read state. Build them before the features that need them.

# DATA FILES

- Everything the shipped application loads at runtime lives in `assets/`, never in `code/`. Code
  references assets by path or by a generated manifest; it never embeds them.
- **Fill in:** the folders under `assets/` and what each holds, and which generator in
  `code/tools/` produces the generated ones.
- Values that define behaviour live in the data files here and exist nowhere else. Generated
  headers and blobs are outputs, never inputs, and are not committed, so a product can replace
  `assets/` wholesale without touching `code/`.
- Test vectors, golden traces and fixtures live in `test/data/` and are referenced by tests, never
  inlined. Golden outputs are regenerated by an explicit command, not as a side effect of a test
  run.
- Third-party sources we compile are pinned in `code/extern/`, one directory per dependency, noted
  in that directory's README. Prebuilt binaries and SDKs go in `extern/bin/`.

# {{PROJECT}}: gates, locations and briefs

The project-specific half of the job queue's rules. `README.md` is the generic half. The planner
keeps this file current; workers follow it.

## Categories

The categories - what each is for, the files it owns, its exceptions, and the shared files every category
may add to - are in [categories.json](categories.json); the worker types are the pool file's
(see `Worker:` on a card). A category whose definition changes while it has cards in `queue/` or
`active/` is held by the scheduler until the developer accepts the change on its page.

## Gates

Run before every push, all of them:

{{GATES}}

## Where things live

| Path | Holds |
|---|---|
| `docs/design/gdd/` | The intent: what the project must do. A card cites the section it implements. |
| `docs/design/tdd/` | The technical design: architecture, invariants, module responsibilities. |
| `docs/conventions.md` | Where files go and how code is written. Every card is worked under it. |
| `docs/reference/` | Specifications and manuals, with an index. The authority for behaviour. |
| `docs/known_gaps.md` | Known differences from the specification, and assumptions made. |
| `code/src/` | The application, one folder per module, interfaces first; OS code under `platform/<platform>/`. |
| `code/lib/` | Static libraries linked by both the application and the tools. |
| `code/tools/` | The generators and harnesses; their output goes to `bin/`. |
| `code/extern/`, `extern/bin/` | Third-party source we compile, and prebuilt binaries. Never mixed. |
| `assets/` | What the application loads at runtime, including the data files that hold behaviour numbers. |
| `test/unit/`, `test/integration/` | Tests, mirroring the `code/` paths; integration tests drive the real binary. |
| `test/data/` | Vectors and goldens, referenced by path, never inlined. |
| `build/` | The build definition: the only source of truth for how anything compiles. |
| `build/`, `out/` | Build intermediates (delete at will) and built packages. Git-ignored, never edited by hand. |
{{LOCATIONS}}

## Standing briefs

{{BRIEFS}}

## Schedule

The intended order, category by category. The order cards are actually taken in follows the README's step
2: dependencies, waiting cards and priority reorder it.

| Plan | Cards |
|---|---|

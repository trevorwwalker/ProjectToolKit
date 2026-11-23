# {{PROJECT}} - Technical Design

Status: draft v0.1 · Owner: architecture · Companion to
[{{PROJECT}}.md](../gdd/{{PROJECT}}.md) · Follows [conventions](../../conventions.md)

This is the **architecture** half of the human-led skeleton: it decides how the thing is built so
that a worker can fill in the headers and stubs without making further architectural decisions.
Where the intent document and this one disagree, this document wins on technical parameters and the
intent document wins on what the product should do.

**What this doc decides:** layering, the invariants, data and state layout, execution order, the
interfaces between the modules, the build, the test architecture, the debugger surface, and the
file-by-file responsibility map.

**What this doc does not decide:** the values and rules the intent document owns, anything marked
`OPEN` in the open questions, and anything a card has not asked it to settle.

## 1. Toolchain

| Item | Choice | Notes |
|---|---|---|
| Language | **fill in** | The standard, and what is forbidden where. |
| Compiler / SDK | **fill in** | The primary path, and the alternatives that are CI targets, not blockers. |
| Build | **fill in** | The build definition lives in `code/` beside what it builds; intermediates go to `build/`, results to `out/`. |
| Third-party | **fill in** | What is vendored in `code/extern/`, what is prebuilt in `extern/bin/`, and what is banned. |
| Tests | **fill in** | The runner and how one filtered test is run in under a second. |
| Formatting / linting | **fill in** | The config at the root and the target that runs it. Style is enforced by tooling, not memory. |

Flag a deviation from the obvious reading of the stack, with the reason.

## 2. Architecture overview

One diagram of the layers and the direction of the arrows.

### 2.1 Dependency edges (enforced)

The table the build checks, not a suggestion: each module, what it may depend on, and what must
never reach into it. If a line in a lower module needs a higher one, the design is wrong.

## 3. The invariants

The numbered rules (I1, I2, ...) the core must never break, written as requirements rather than
goals. For each: the rule, why it exists, how it is checked (`static_assert`, a test, a lint), and
the symptom that says it broke silently.

## 4. Data and state layout

The state's shape: fixed or growing, where it lives, who owns it, what may hold a reference to it,
and how it is serialised and hashed. Say what is forbidden inside it (heap, pointers into other
state, a wall clock, an OS handle).

## 5. Execution order

The order everything runs in, in one place: the tick or frame, the systems in sequence, the sorting
rules, and where input, timing and rounding are decided. One fixed order, in one function.

## 6. Module responsibilities

One section per module under `code/src/`: what it owns, what it exposes, what it may depend on, and
what it must never do. Interfaces are pure; no OS call, handle, socket, clock read, file handle,
thread or graphics context appears in an interface header. OS-specific code goes in
`code/src/platform/<platform>/`, shared code in `code/src/platform/common/`.

## 7. The debugger protocol

What an agent can ask a running build, and the answers it gets back: state queries by readable path,
run, pause, step, run-until, breakpoints and watchpoints, snapshots, tracing, and machine-readable
(usually JSON) responses. This is the feedback loop the whole workflow leans on; design it before
the features.

## 8. The MCP surface

The tools that speak that protocol, their arguments and their output limits. Keep output compact:
summarise and truncate with markers rather than dumping. Return errors an agent can act on, and make
a retry harmless.

## 9. Snapshots and tracing

How a failure is captured and replayed: what a snapshot holds, how a trace is recorded, and how two
runs are diffed to the first divergence.

## 10. Build system

The configurations (debug, release, headless/stub), the presets that define them, the options that
swap a real backend for a stub, and the fact that the headless build has no third-party dependency
at all and must never break.

## 11. Test architecture

Which tests live in `test/unit/`, `test/integration/` and `test/data/`; how a test names the
invariant it checks; what a golden output is and the command that regenerates it; the coverage
target for the core. Every test here was preceded by a plan saying what it proves.

## 12. Content pipeline

How `assets/` becomes what ships: the generator tools in `code/tools/`, their output folders, and the
rule that generated artifacts are outputs, never inputs, and are not committed.

## 13. Repository map

The file-by-file map: path, what lives there, whose lane owns it. This is what makes two workers
editing the same file impossible rather than unlucky.

## 14. Milestones, technically

The build order that matches the milestones in the intent document: what compiles first, what is
testable at each step, and what is deliberately stubbed.

## 15. Open technical questions

Numbered `OPEN-n`, each with what is undecided, what it blocks, and what would settle it. A worker
that hits one parks instead of guessing.

## 16. Reading order for an agent

The list of documents, in order, that a worker should read before its first card in each module -
and the one line that tells it what to do when this document and the code disagree.

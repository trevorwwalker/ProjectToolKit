# {{PROJECT}}: conventions

<!-- ptk-layout: library -->

{{PROJECT}} is a library project, laid down by ProjectToolkit's `-Action new-library`. The line above marks it:
`-Update` and `-Action new-project` never add the folders a library project leaves out. Keep it.

## The layout

```
bin/            scripts
code/lib/       one folder per library: code/lib/<name>/
code/tools/     the library's tools
test/unit/      tests
docs/           conventions (this file) and docs/design/
jobs/           the job queue
out/            build output (ignored by git)
```

There is no `assets/`, `code/src/`, `code/extern/`, `extern/`, `test/integration/` or `test/data/`. Add one
only when this library needs it, and say so here.

## Rules for nesting

1. A host nests this repository at `code/extern/<repository>/`. The README says whether the host holds it pinned
   to a commit or following its main.
2. The nested copy's `jobs/` is never worked from a host: this library's cards are worked only in its own clone,
   as its own pool project.
3. A host's categories (`jobs/categories.json`) leave the nested folder out: no host card owns
   `code/extern/<repository>/`.

## This library's own decisions

**fill in**: language, build, naming, formatting, errors, logging, and where the public interface is.

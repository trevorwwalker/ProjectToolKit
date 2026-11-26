# {{PROJECT}}

**fill in**: what this library is and what it is for, in a few sentences.

{{PROJECT}} is a library project: a repository of its own that other projects (hosts) nest at
`code/extern/<repository>/` and build from. It has its own job queue and is worked only in its own clone.
The layout and the rules for nesting are in [docs/conventions.md](docs/conventions.md).

## What is here

- `code/lib/<name>/`: one folder per library.
- `code/tools/`: the library's tools.
- `test/unit/`: its tests.
- `docs/`: its conventions and design.
- `jobs/`: its job queue.
- `bin/`: its scripts.

## For a host

- **Language and build**: **fill in** (the kit carries none; this library decides).
- **Public interface**: **fill in** (where a host finds the headers, modules or API it uses).
- **How a host holds it**: **fill in** one of: *pinned* (a git submodule at a commit, updated on purpose) or
  *following* (a copy that follows this repository's main).
- **Nesting**: a host puts this repository at `code/extern/<repository>/`. Its `jobs/` there is never worked
  from the host, and the host's categories leave that folder out.

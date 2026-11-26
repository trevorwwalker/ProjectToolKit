# Library projects

[Guide index](README.md) · Related: [projects.md](projects.md), [scripts.md](scripts.md)

A **library project** is a repository of its own that holds one or more static libraries and the
tools that go with them, and that other projects **nest** and build from &mdash; the way a link folder
is nested in unix. It has its own conventions, its own design docs and its own job queue, exactly like
any other project; it just carries fewer folders and no application. The toolkit lays one down with
`projecttoolkit.ps1 -Action new-library`.

This page says when to make one, how to create it, what to decide and write down first, how a host
nests it (the two ways), how a host moves to a newer version of it, and how its queue runs. The
rules themselves are in ["Library projects" in `docs/conventions.md`](../conventions.md#library-projects);
this page shows the commands.

## When to make a library project

Code shared between parts of one program is a **static library under that project's `code/lib/`**, not a
project of its own. Make a **library project** &mdash; a separate repository nested by hosts &mdash; when:

- **two or more projects use it.** Then it has one home and its own history, instead of a copy in each
  project that drifts; and
- **or it has its own tools and docs.** A packer that belongs with the format it reads, or a library
  whose design deserves its own plans and cards, lives better in its own repository with its own queue.

A library project is a whole project: it gets its own plan, its own cards and its own Switchyard run.
If the "library" is only ever used by one application, keep it in that application's `code/lib/`.

## Create one

```powershell
& "$env:LOCALAPPDATA\Programs\ProjectToolkit\bin\projecttoolkit.ps1" -Action new-library -Repo C:\Projects\MyLib
```

From a clone of the toolkit, call `C:\Projects\ProjectToolkit\bin\projecttoolkit.ps1` instead. The folder
is created if it does not exist and `git init`-ed on branch `main`. The command prints what it created
and a numbered "Next" list. The parameters:

| Parameter | Default | Meaning |
|---|---|---|
| `-Repo` | (required) | The library's folder. Created and `git init`-ed if needed. |
| `-Project` | the folder name | The library project's name (the one the pool file will carry). |
| `-Categories` | `A=library:code/,test/` | The categories, as `LETTER=name:path,path;...`. A library needs only the one to start; give `-Categories` when you know better. |
| `-Gate` | none | The commands a worker must pass before every push (build, test). `{jobs}` becomes the thread count the scheduler allows. |
| `-Model`, `-Endpoint` | none | Point the Copilot worker at a local model server. Left out, workers use GitHub's own models. |
| `-JobsEnvVar` | `AGENT_JOBS` | The environment variable that tells a worker its thread count. |
| `-Force` | off | Overwrite files that already exist. Without it nothing already there is touched. |
| `-Update` | off | Refresh the kit's own text and fill in missing queue files, like any `-Action jobs -Update`. |
| `-JoinPool` / `-NoPool` | joins | The pool join at the last step; `-NoPool` skips it (use it for experiments). |

`-Modules` means nothing to a library (it has no `code/src/`), and no language, build file or example
code comes with it: the library decides those itself, below.

The tree it lays down (`mylib/`):

```
mylib/
├── bin/                 # the library's own scripts and built tools
├── code/
│   ├── lib/             # one folder per library: code/lib/<name>/
│   └── tools/           # source for the tools that go with the libraries
├── docs/
│   ├── conventions.md   # the library's own decisions; carries the library marker line
│   ├── design/          # the library's design documents, when it writes them
│   ├── known_gaps.md
│   └── planner-brief.md
├── jobs/                # its own queue (queue, active, review, done, plans, archive, requests)
├── out/windows/         # build output; git-ignored, kept present by a .gitkeep
├── test/unit/           # its tests
├── .gitattributes
├── .gitignore
└── README.md            # what it is, its honest status, and how hosts hold it
```

There is no `assets/`, `code/src/`, `code/extern/`, `extern/`, `test/integration/` or `test/data/`, and
none is ever added back by `-Update`: the line `<!-- ptk-layout: library -->` near the top of the
library's `docs/conventions.md` marks it as a library project. **Keep that line.** A library adds one of
the left-out folders the day it genuinely needs it, under the canonical name, and says so in its
conventions.

## What to decide and write down first

The new `README.md` has four **fill in** lines under "For a host". They are the library's first
decisions, and a host's author reads them before writing a single git command:

- **Language and build.** The kit carries none: C, C++, Rust, a CMake tree or a .NET solution &mdash; the
  library decides, and says how its libraries and tools are built.
- **Where the public interface is.** The folder a host includes, imports or links against
  (headers, a module map, the one public API) &mdash; and what a host must not reach into.
- **Pinned or following.** Whether hosts hold the library pinned to a commit (a git submodule) or
  following its `main` (a git-ignored clone). This is a decision for the library, not for the kit; the
  two ways and their commands are below.
- **How a host nests it.** The README already says: at `code/extern/<repository>/`. Keep that line
  true to how the library actually wants to be held.

Write the rest in the library's own `docs/conventions.md`: naming, formatting, errors, and the rules
its tools follow. Then follow the printed "Next" list: fill in `jobs/project.md` and `jobs/scheduler.json`,
have a planner write the library's plan in `jobs/plans/`, commit and push to `main`, and join the pool.

## Nest it into a host

From the host's side the library is third-party source we compile ourselves, so it goes to
`code/extern/<library repository>/` ("Third-party code" in the conventions) and the host's build
compiles it from there. The library's `README.md` says which of the two ways its hosts use; both
commands below work either way. Each git command is explained in one line.

### Pinned: a git submodule

The host records one exact commit of the library and moves it only on purpose.

```powershell
git submodule add <library url> code/extern/mylib
git commit -m "nest mylib as a submodule (pinned)"
```

- `git submodule add <url> <path>`: clones the library into `<path>`, records its url and path in the
  host's `.gitmodules`, and stages the library's current commit; it is a normal edit until you commit.
- `git commit -m ...`: puts both `.gitmodules` and the recorded commit in the host's history, so every
  host clone knows which library commit to fetch.

Anyone with a fresh clone of the host then gets the library with:

```powershell
git clone --recurse-submodules <host url>
```

- `git clone --recurse-submodules`: clones the host and, straight after, clones each submodule and
  checks out the commit the host recorded.

In a host clone that already exists (someone cloned without the flag, or the library is new upstream):

```powershell
git submodule update --init
```

- `git submodule update --init`: registers the submodules named in `.gitmodules` (`--init`) and checks
  each one out at its recorded commit (`update`).

### Following: a copy that tracks the library's main

The host records no version; the nested folder is its own clone that the host's git ignores.

```powershell
git clone <library url> code/extern/mylib
```

- `git clone <url> <path>`: makes a full clone of the library at `<path>`, on its `main`.

Then add the line `code/extern/mylib/` to the host's `.gitignore`, and commit the host as usual.

- editing `.gitignore`: tells the host's git to ignore the folder entirely, so the nested copy never
  lands in a host commit; every fresh clone of the host repeats the `git clone` above (say so in the
  host's `docs/conventions.md`).

### Try it with two local folders first

Before pointing a real host at a real library, rehearse both ways in scratch folders: any two local git
repositories will do, and no Switchyard or pool is involved. One catch:
git treats cloning from a *folder path* as the blocked `file` transport inside the recursive submodule
commands, so those need `-c protocol.file.allow=always` on the command line:

```powershell
git -c protocol.file.allow=always submodule add C:\scratch\mylib code/extern/mylib
git -c protocol.file.allow=always clone --recurse-submodules C:\scratch\myhost C:\scratch\hostclone
git -C C:\scratch\hostclone -c protocol.file.allow=always submodule update --init
```

With real https urls the flag is unnecessary; leave it out of your host's scripts.

## Move the host to a newer version of the library

### Pinned

```powershell
git -C code/extern/mylib fetch
git -C code/extern/mylib checkout <commit or tag>
git add code/extern/mylib
git commit -m "mylib to <commit>"
```

- `git -C <path> fetch`: downloads the library's new commits into the nested clone; it changes nothing
  that is checked out.
- `git -C <path> checkout <commit or tag>`: selects the version to build &mdash; the working copy moves to
  that exact commit (a detached HEAD, which is normal for a submodule).
- `git add code/extern/mylib`: stages the *new recorded commit* (the folder, not its files: git
  records just the id).
- `git commit -m ...`: records it, so the host's history now says which library commit this is.

Build and test the host **before** that last commit: the point of pinning is that a host commit always
names a library version that built and passed.

### Following

```powershell
git -C code/extern/mylib pull --ff-only
```

- `git -C <path> pull --ff-only`: fetches the library's `main` and moves the copy straight to it, and
  refuses (non-zero exit, "commit your changes or stash them") if the copy was changed by hand where
  the update would overwrite it.

A host that follows gives up a promise: its history no longer says which library version a given build
used &mdash; the same host commit built on two days can contain different library code. A host that must
reproduce a build holds the library pinned instead.

## Run its queue

A library project is an **ordinary pool project**: it is listed in the pool file, has its own
`jobs/scheduler.json`, and Switchyard works its cards like any other project's. Three things to keep
straight:

- **Its cards are worked only in its own clone.** The nested copy under a host's `code/extern/` has a
  `jobs/` folder too &mdash; it is the same text &mdash; and it is **never** worked from the host. A change you
  want in the library is a card in the library's own queue, run in its own worktrees; it reaches the
  host through the update commands above.
- **The host's categories leave the nested folder out.** Put an `except` for `code/extern/<library repository>/`
  in the host's `jobs/categories.json` (on any category that would otherwise own `code/`):
  `{"A": {"owns": ["code/", "test/"], "except": ["code/extern/mylib/"]}}`. Then a host worker reads the
  library and never changes it.
- **The library's default category is one and broad** (`A=library:code/,test/`). Split it into real
  categories with the planner as the library grows, the same as for any project.

Setup commands for the queue itself are in [projects.md](projects.md); the script's other actions are
listed in [scripts.md](scripts.md).

# Worker agents: the contract, and Codex CLI and OpenCode

Card 1150-A, 2026-11-23. Design only: nothing here is built. Plan: `jobs/plans/agents_plan.md`, section 1. Rule IDs
`AG-n` are stable names for test cases, like the spec's (`spec.md`).

Sources: the code in `Scheduler.Core/Config/Agents.cs`, `Scheduler.Core/Config/SchedulerConfigLoader.cs`,
`Scheduler.Core/Contracts/AgentDefinition.cs`, `Scheduler.Core/Launch/` (`WorkerArgs`, `WorkerCommandLine`,
`WorkerEnv`, `WorkerFiles`, `WorkerPrompt`, `MessageHook`), `Scheduler.Core/PageModel/WorkerEvents.cs` and
`WorkerLogScan.cs`, `Scheduler.Core/Results/RunResults.cs`, `Scheduler.Core/Categories/WorkerSelection.cs`,
`Scheduler.Host/Processes/WorkerLogRenderer.cs` and `WorkerEventRender.cs`, `Scheduler.Host/Program/MessageDelivery.cs`
and `Scheduler.Host/Tick/TickHost.cs`; spec rules CFG-9 to CFG-12, WRK-4, WRK-8, WRK-11, WRK-12, WRK-14, TIME-14,
TIME-15 and WT-9.

**What is confirmed and what is not.** Section 1 is read from the code. Sections 2 and 3 were first written without
the web and without either CLI; on 2026-11-23 the developer installed both (`codex-cli 0.171.0`, `opencode 2.4.1`,
through npm) and they were **checked against `--help` and the strings in the installed programs** (flags, event names
and fields, config keys, error text, exit code handling). Points marked *(checked)* come from that; points still marked
*(unconfirmed)* need a run. Later that day Codex was signed in (ChatGPT account) and OpenCode ran one of its own
provider's free models with no sign-in, and a good run and a failing run of each were **recorded** into the fixture
folders (section 5); points marked *(recorded)* come from those runs, and where a run disagreed with the help text this
file now follows the run. The developer's choices are in the
card's Questions: Codex runs without its sandbox, like Claude workers, and its no-web gap goes into `known_gaps.md`.

## 1. The contract today

A worker is started as one command line and judged from its log and exit code. There are two agent CLIs: GitHub Copilot
CLI (format `copilot`) and Claude Code (format `claude`); any other command runs as format `text`.

### 1.1 What the scheduler needs from an agent

| Need | How it is met today | Where |
|---|---|---|
| A one-shot, unattended run | `cli` plus `args` from the agent's config entry (`worker` section for the default agent, `agents.<name>` for the rest); `{prompt}` is one argument | `AgentDefinition`, `SchedulerConfigLoader.ReadAgents` |
| The prompt | Never the brief itself: `{prompt}` becomes a one-sentence *pointer* naming the prompt file, because `cmd.exe` takes 8191 characters | `WorkerFiles.Names`, `WorkerArgs.For` |
| The model | `{model}` in the args or env (worker's, else agent's, else config's) | `Agents.Expand`, `WorkerSelection` (WRK-4) |
| A model server | `{endpoint}` in args or env; an agent of format `claude`, or with `"endpoint": ""`, uses its own service | `WorkerSelection` (WRK-4) |
| Environment | the agent's `env` with placeholders filled (`{contextLength-N}` from the server); keys that only *other* agents set are removed; `PTK_JOBS`-style jobs variable, `PTK_MSG_FILE`, the lock variables (`core.hooksPath` through `GIT_CONFIG_*`) are added | `WorkerEnv.Build` |
| Start | `cmd /c title ... & ping -n 2 127.0.0.1 >nul & <cli> <args> > "<raw log>" 2>&1` (WRK-12): stdout and stderr in one file, in order; started inside a job object in the worktree | `WorkerCommandLine`, `TickHost` launch |
| Output | format `copilot` or `claude`: one JSON event per line in `<card>-<time>.jsonl`, rendered into `<card>-<time>.log`; format `text`: the raw log *is* the readable log | `WorkerFiles.Names`, `WorkerLogRenderer` |
| Live view | each new raw line becomes `LiveEvent`s with a mark: `~` reasoning, `*` message, `>` tool call, `x` failed tool, `!` error, slow model call, session end | `WorkerEvents.Render` (page), `WorkerEventRender` (readable log) |
| Stats (PAGE-10) | calls, tools, failed tools, context characters, compactions, generated tokens and time, last tokens/s and time to first token | `WorkerLogScan.Copilot` / `.Claude` |
| Result | the last `LANE-RESULT: <word>`; in a rendered log only on lines starting `* `, which only the model's own message text gets (TIME-14) | `RunResults.ResultOf` |
| Quota | with no result line: a quota pattern (whole word, any case) on a non-JSON line or a JSON *error* line (`"type":"...error..."`, `"is_error":true`, `"subtype":"error`, a failed `result`) - never the model's text (TIME-15). Patterns are the agent's `quotaPatterns`, else the defaults (`usage limit`, `rate limit`, `Failed to authenticate` for a named agent) | `RunResults.QuotaHit`, `TickHost` finish (WT-9) |
| Exit | a non-zero exit with no result is `crashed` | `RunResults.EndReasonOf` |
| No web | Copilot: `--excluded-tools` and shell denies; Claude: `--disallowedTools`; any other CLI gets nothing (WRK-11) | `WorkerArgs.For` |
| Messages | Claude: a `PostToolUse` hook (`--settings <file>`) runs `scheduler-next.exe --deliver-message claude`, which prints `additionalContext`; every other agent reads `msg-slotN.txt` itself, as its prompt says (WRK-14) | `MessageHook`, `MessageDelivery`, `WorkerPrompt` (`Hooks`) |
| Locks | git hooks only (`commit-msg`, `pre-push` through `core.hooksPath`): no agent support needed | `WorkerLocks` |
| Usage | not recorded per run: accounting.csv has no token or cost column; the live view shows Claude's `total_cost_usd` at session end | `AccountingRow`, `WorkerEvents.RenderClaude` |

### 1.2 Where adding an agent touches the code

An agent that only needs `cli`, `args`, `env`, `model`, `endpoint` and `quotaPatterns` is config, not code: it runs today
as format `text` (no live view beyond the raw text, no stats, the result read from any line). An agent with its own
event format touches each of these places, because each one decides by `AgentFormat`:

1. `Contracts/AgentFormat.cs`: the enum value.
2. `Config/Agents.cs`: `FormatOf` (the config name), `GuessFormat` (the guess from the args).
3. `Launch/MessageHook.cs`: `Delivers`, `FormatName`, `ParseFormat`, `Reply`; `Host/Program/MessageDelivery.cs`.
4. `Launch/WorkerArgs.cs`: the no-web switches and the hook switch.
5. `Launch/WorkerFiles.cs`: json or not (any non-`text` format is json today, which is right).
6. `Categories/WorkerSelection.cs`: own service (`Format == Claude`).
7. `PageModel/WorkerEvents.cs`: `Render` is `Claude ? RenderClaude : RenderCopilot` - **a new format falls into the
   Copilot parser** and shows nothing useful.
8. `Host/Processes/WorkerEventRender.cs`: the readable log, a second parser of the same events. It must give `* ` to
   exactly the model's message lines, or TIME-14 finds no result.
9. `PageModel/WorkerLogScan.cs`: the stats, a third reader of the same events.
10. `Host/Processes/WorkerLogRenderer.cs`: picks scan and render with `== Claude` ternaries.
11. `Results/RunResults.cs`: `QuotaHit`'s JSON-error regex knows Copilot's and Claude's error lines only.
12. `Host/Tick/TickHost.cs`: `FormatOf` (text with a separate readable log reads as Copilot) - no change needed.

So a new event format costs about eleven files, and three of them (7, 8, 9) parse the same lines three ways.

### 1.3 Recommendation: an agent protocol per format, not a data table

A *data* table (a JSON file of field names per agent) cannot express the parsing: Codex nests its text in `item`,
OpenCode in `part`, Claude in `message.content[]`, and what counts as a failed tool differs each time. The config is
already the data table for what can be data (`cli`, `args`, `env`, `quotaPatterns`, `endpoint`).

What would make the next agent cheap is one class per format behind one interface in Core, found by format name:

```
interface IAgentProtocol
  Name                                   // "copilot", "claude", "codex", "opencode", "text"
  bool Guess(cli, args)                  // CFG-10's guess
  IReadOnlyList<string> NoWeb            // WRK-11 switches (args), may be empty
  bool OwnService                        // WRK-4
  HookSupport Hooks                      // none, or the switch, settings and reply (WRK-14)
  IReadOnlyList<LiveEvent> Events(line)  // one parser: page live view AND readable log
  void Scan(line, WorkerStats)           // the stats
  bool IsErrorLine(line)                 // TIME-15's JSON error test
```

The readable log becomes `LiveEvent.Mark` + text (it already nearly is: `*`, `~`, `>`, `x`, `!`), so items 7 and 8 merge
into one parser and the two can no longer disagree. The registry replaces the switches in items 2, 3, 4, 6, 10 and 11.
Recommended as the **first step of the Codex card** (plan section 2), moving Copilot and Claude behind it with their
existing tests unchanged, so that Codex and OpenCode are each one new class, its fixtures and its tests. Not built here.

## 2. Codex CLI

OpenAI's agent CLI (open source, Rust). Checked against `codex-cli 0.171.0` (`codex exec --help`, `codex features
list` and the program's strings); what only a run can show is marked *(unconfirmed)*.

- **Install (Windows).** `npm install -g @openai/codex` *(checked: installed so, `codex` / `codex.cmd` on the npm
  PATH)*. Native Windows; the old experimental Windows sandbox features are marked removed in this version, which does
  not matter as the workers run without the sandbox.
- **Auth.** Subscription: `codex login` opens a browser to sign in with a ChatGPT account (Plus, Pro, Business);
  credentials are kept under `%USERPROFILE%\.codex\` (`auth.json`; `CODEX_HOME` moves the folder). API key:
  `codex login --with-api-key` (reads the key from stdin), or `OPENAI_API_KEY` / `CODEX_API_KEY` in the environment for
  `codex exec`. The scheduler sets neither: the developer signs in once, as for the other agents.
- **Headless command.** `codex exec [flags] "<prompt>"`: runs to the end and exits *(checked)*. `--json` prints one
  JSON event per line on stdout; progress text goes to stderr (both land in the raw log). `-o <file>` writes the last
  message to a file. `--skip-git-repo-check` allows a folder that is not a git repository (a worktree is one, so it is
  a safety only). `-C <dir>` sets the folder; the scheduler starts it in the worktree already. Also useful:
  `--ephemeral` (no session files kept), `--ignore-user-config` (skip the developer's `config.toml`; sign-in still
  works), `--add-dir <dir>` (more writable folders, for the sandbox case only).
- **Stdin** *(checked, help text)*: "If stdin is piped and a prompt is also provided, stdin is appended as a `<stdin>`
  block". The scheduler's `cmd /c ... > log 2>&1` (WorkerCommandLine) leaves stdin as the process gets it; if that is
  an open pipe, Codex would wait for it to close. The Codex line therefore ends the CLI part with `< nul` (AG-17).
  *(recorded)* With `< nul` the run ends on its own, but Codex still prints the plain-text line `Reading additional
  input from stdin...` first (to stderr, so it lands in the raw log); AG-3 ignores it.
- **Model.** `-m {model}` (for example a `gpt-5` codex model); reasoning effort with `-c model_reasoning_effort=high`.
  `-c key=value` sets any config key for the one run, which is how the scheduler should pass settings (nothing written
  into the developer's `config.toml`).
- **Approvals and sandbox.** In `exec` the approval policy is *never ask*. The sandbox is `--sandbox read-only |
  workspace-write | danger-full-access`. `workspace-write` blocks the network by default, so `git push`, `git pull` and
  `dotnet restore` fail unless `-c sandbox_workspace_write.network_access=true`; it also allows writes only in the
  folder and temp, while a worker writes the moves folder and the log folder under `%LOCALAPPDATA%`.
  `--dangerously-bypass-approvals-and-sandbox` runs with no sandbox, like Claude's bypass mode *(all checked)*.
  **Decided** (developer, 2026-11-23): Codex workers run with the bypass, like Claude workers.
- **No web (WRK-11).** The web search tool is set by the top-level key `web_search` = `"live"`, `"indexed"`,
  `"cached"` or `"disabled"` *(checked; the first draft's `tools.web_search` was wrong)*, so the no-web args are
  `-c web_search="disabled"`. Codex has command rules (`.rules` files, skipped with `--ignore-rules`), but no per-run
  deny for `curl` in the shell is known to work with the bypass: no-web for shell downloads rests on the prompt alone.
  **Decided** (developer, 2026-11-23): accepted, and the Codex card writes it into `docs/known_gaps.md`.
- **Events (`--json`).** Each line has a `type` *(names and fields checked in the program, and the rows marked † seen
  in the recorded runs; a recorded good run is `thread.started`, `turn.started`, then `item.started` /
  `item.completed` pairs, one `turn.completed`)*:

  | Event | Fields used | Scheduler meaning |
  |---|---|---|
  | `thread.started` † | `thread_id` | nothing (the session id, not shown) |
  | `turn.started` † | - | a model call starts (stats `Calls`) |
  | `item.started` † / `item.updated` | `item` | nothing until complete; a `command_execution` start may be shown as the tool call |
  | `item.completed`, `item.type = agent_message` † | `item.text` | `*` message lines (TIME-14 reads these) |
  | `item.completed`, `item.type = reasoning` | `item.text` | `~` reasoning (a summary, not the full thinking) |
  | `item.completed`, `item.type = command_execution` † | `item.command`, `item.exit_code`, `item.status`, `item.aggregated_output` | `>` tool call; `x` failed tool when `exit_code != 0` or `status = failed`, with the first two output lines (recorded: `dotnet --no-such-switch` gives `exit_code` 1 and `status` `failed`; `item.command` is the whole shell line, `"<home>\...\pwsh.exe" -Command '<command>'`, so the label strips that wrapper) |
  | `item.completed`, `item.type = file_change` | `item.changes[].path`, `.kind` | `>` tool call (`edit: <paths>`) |
  | `item.completed`, `item.type = mcp_tool_call` / `web_search` / `todo_list` | `server`, `tool` / `query` | `>` tool call / nothing |
  | `item.completed`, `item.type = error` † | `item.message` | `!` warning only: the run goes on (recorded: "Model metadata for `no-such-model` not found. Defaulting to fallback metadata ..." before the real failure) |
  | `turn.completed` † | `usage.input_tokens`, `usage.cached_input_tokens`, `usage.output_tokens` | session end line with tokens; stats `GenTokens` += output tokens |
  | `turn.failed` † | `error.message` | `!` error, `session failed: ...`; a quota line when the text matches |
  | `error` † | `message` | `!` error; a quota line when the text matches |

- **Usage.** Tokens per turn (above; `reasoning_output_tokens` and `cache_write_input_tokens` also exist, *recorded*);
  no cost. The recorded run was one turn.
- **Quota text.** Subscription *(checked)*: "You've hit your usage limit." with variants ("... for ...", "Upgrade to
  Plus to continue using Codex", "To get more access now, send a request to your admin"); the program's error kinds
  include `QuotaExceeded` and `UsageNotIncluded`, and its retry code treats "429", "rate limit" and "too many requests"
  as rate limits. API key *(unconfirmed, the service's own words passed through)*: "Rate limit reached",
  "insufficient_quota", "You exceeded your current quota". Proposed `quotaPatterns`: `usage limit`, `rate limit`,
  `too many requests`, `insufficient_quota`, `exceeded your current quota`. They match only on `error` / `turn.failed`
  lines or non-JSON lines (AG-8).
- **Exit codes.** *(recorded)* 0 when the turn completes; 1 after `turn.failed` (the unknown model). A bad flag or a
  fatal start error is non-zero too *(unconfirmed)*.
- **Instructions files.** Codex reads `AGENTS.md` from the repository (not `CLAUDE.md`). The worker's prompt is the
  whole brief, so nothing depends on it; a project's `AGENTS.md`, if any, is added to every Codex worker's context.
- **Windows quirks.** *(recorded)* The shell tool runs each command as `pwsh.exe -Command '<command>'` (PowerShell 7
  from the user's PATH, here its WindowsApps alias), so a raw log holds the user's home path and fixtures are scrubbed
  (5.1). The model may put several commands in one call (`a; b; c`): the first recording did, and the shell's exit
  code was then the last command's, hiding the failure; the recording prompt therefore asks for one command per call
  (5.2). Long-running commands get Codex's own per-command timeout *(unconfirmed)*. Stdin: see above; with `< nul`
  (AG-17) the recorded runs under `cmd /c` ended on their own.
- **Messages (WRK-14).** *(checked; corrects the first draft)* Codex has Claude-style hooks: the `hooks` feature is
  stable and on by default; events include `PreToolUse`, `PostToolUse`, `SessionStart`, `UserPromptSubmit` and `Stop`;
  hooks are read from `hooks.json` (the user's `%USERPROFILE%\.codex\` and a project's `.codex\`); and the
  `PostToolUse` command output takes `hookSpecificOutput.additionalContext`, the same shape as Claude's. So the
  scheduler's message hook can serve Codex too. What is not known is a per-run way to install it like Claude's
  `--settings` file: a project `.codex\hooks.json` must be trusted (`--dangerously-bypass-hook-trust` skips that for
  one run) and would sit in every worktree; the user-level file would touch the developer's own setup. The Codex card
  therefore ships with `Hooks` false (workers read `msg-slotN.txt` themselves, as Copilot's do) and a follow-up card
  turns the hook on once a recorded run shows it firing (AG-13).
- **Endpoint.** Codex uses OpenAI's service: its config entry has `"endpoint": ""` (own service, WRK-4), or it would
  be handed the config's local model server. (Codex can reach a local server through `-c model_provider=...` or
  `--oss`; not part of this plan.)

Proposed config entry (the shape the Codex card builds and tests; values to confirm from the recording):

```json
"codex": {
  "cli": "codex",
  "format": "codex",
  "endpoint": "",
  "args": ["exec", "--json", "--skip-git-repo-check", "--ephemeral",
           "--dangerously-bypass-approvals-and-sandbox", "-m", "{model}", "{prompt}"],
  "quotaPatterns": ["usage limit", "rate limit", "too many requests", "insufficient_quota",
                    "exceeded your current quota"]
}
```

With web off the scheduler adds `-c web_search="disabled"` (AG-11); the command line closes stdin (AG-17).

## 3. OpenCode

The open-source agent CLI and terminal UI (`opencode`, any model provider). Checked against `opencode 2.4.1`
(`opencode --help`, `opencode run --help` and the program's code); what only a run can show is marked *(unconfirmed)*.

- **Install (Windows).** `npm install -g @opencode/cli` *(checked: installed so, `opencode` / `opencode.cmd` on the
  npm PATH; not the first draft's `opencode-ai`)*. Its upgrade needs Git for Windows (or
  `OPENCODE_GIT_BASH_PATH`), see Windows quirks.
- **Auth.** `opencode auth` manages "integrations and credentials" *(checked; `opencode auth list` said "No
  authenticated integrations" here)*; or the provider's own variable (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, ...).
  Which subscription sign-ins are offered is *(unconfirmed)*. A local model server needs no key. *(recorded)* With
  nothing signed in, `opencode models` still lists the OpenCode provider's own free models (`opencode/<name>-free`),
  and `-m opencode/<name>-free` runs with no key: that is how the fixtures were recorded.
- **Headless command.** *(checked)* `opencode run [flags] [<message...>]` runs one prompt to the end and exits.
  `--format json` prints JSON events, one per line. `-m provider/model#variant` sets the model, `--agent <name>` the
  agent (default `build`, which may edit and run commands). Also: `--auto` approves every permission not explicitly
  denied; `--archived` keeps the session out of session lists ("useful for scripted or CI runs"); `--thinking` shows
  reasoning; `--standalone` runs a private server instead of the shared **background service**, which `run` otherwise
  connects to (and starts). Workers use `--standalone`: one process tree per worker, killed with it, no service left
  running between cards (AG-18).
- **Stdin** *(recorded)*: `opencode run` with stdin left as an open pipe printed nothing and did nothing for ten
  minutes (its private server had started); with `< nul` the same run took under a minute. So OpenCode needs the closed
  stdin of AG-17 as much as Codex does.
- **Working folder** *(recorded)*: OpenCode takes its project folder from the `PWD` environment variable when it is
  set, not from the process's working folder. Started from Git Bash (which exports `PWD`) through `cmd /c` in another
  folder, it worked in the Git Bash folder instead. The scheduler must set `PWD` to the worktree in the worker's
  environment (AG-19); `run` has no folder flag.
- **Model.** Always `provider/model`: `-m openai/{model}`, or `-m local/{model}` for the local server below.
- **Approvals.** *(checked)* Permissions are config: `"permission": { "edit": "allow", "bash": "allow", "webfetch":
  "deny", "external_directory": "allow" }`, where `bash` may be a map of command patterns (`{"*": "allow", "curl *":
  "deny"}`) *(pattern form unconfirmed)*. In `run`, a permission left at `ask` is **rejected, not waited on**: the run
  prints "permission requested: ...; auto-rejecting" and tells the model "This non-interactive run cannot ask the user
  for permission". With `--auto` it is approved once instead. The scheduler passes `--auto` and sets the denies
  explicitly, so nothing hangs and the denies hold. `external_directory` matters: a worker writes the scheduler's
  moves folder and log folder outside its worktree. Config is passed for the one run in the environment
  *(checked)*: `OPENCODE_CONFIG_CONTENT` holds a whole config as JSON (or `OPENCODE_CONFIG` names a file). The
  scheduler's `env` already fills `{model}`, `{endpoint}` and `{contextLength-N}` inside such a value (CFG-12, WRK-8),
  so no new mechanism is needed.
- **No web (WRK-11).** `webfetch` (and `websearch` if present) `deny`, and `bash` denies for `curl`, `wget`,
  `Invoke-WebRequest`, `iwr`, `Invoke-RestMethod`, `irm`. These live in the config value, so for OpenCode WRK-11 is met
  through `env`, not args: two env entries (web and no web) or the agent's own `env` choosing by `worker.web`; the
  OpenCode card decides, the simplest being that the scheduler adds the deny entries itself when web is off.
- **The local model server case.** A provider over any OpenAI-compatible endpoint:

  ```json
  { "provider": { "local": { "npm": "@ai-sdk/openai-compatible", "name": "Local server",
      "options": { "baseURL": "{endpoint}" },
      "models": { "{model}": { "name": "{model}", "limit": { "context": {contextLength}, "output": 8192 } } } } } }
  ```

  with `-m local/{model}`. `{endpoint}` already ends in `/v1` *(checked: the config's endpoints are written so, and
  the scheduler's own probe appends only `/models`)*.
  The `npm` package is fetched by OpenCode on first use: **a first run needs the network once**, then works offline.
- **Events (`--format json`).** Each line has a `type` and a `timestamp`; most have a `sessionID` and a `part`
  *(names and fields checked in `run`'s code; rows marked † seen in the recorded runs, where a good run is, per model
  call, `step_start`, then `text` and/or `tool_use`, then `step_finish`. The top-level `type` uses `_`, the
  `part.type` inside uses `-`: `step_start` holds a `step-start` part, `tool_use` a `tool` part)*:

  | Event | Fields used | Scheduler meaning |
  |---|---|---|
  | `step_start` † | `part.messageID` | a model call starts (stats `Calls`) |
  | `text` † | `part.text` (emitted once, when the text part has ended) | `*` message lines |
  | `reasoning` | `part.text` | `~` reasoning (only with `--thinking`, *unconfirmed*; workers leave it off) |
  | `tool_use` † | `part.tool`, `part.state.status`, `part.state.input`, `part.state.output` / `.error`, `part.state.metadata.metadata.exit` | `>` tool call (`shell: <command>`, `edit: <filePath>`; recorded: the command tool is named `shell`; whether the permission key `bash` reaches it, or it needs `shell`, is *unconfirmed*, as `--auto` approved it either way, and the OpenCode card must check it with a denied command before relying on the AG-11 denies); `x` failed tool when `status = error` **or** `metadata.metadata.exit != 0` (recorded: a command that fails has `status` `completed` and its exit code only there) |
  | `step_finish` † | `part.reason` (`tool-calls`, then `stop` on the last), `part.tokens` (`input`, `output`, `reasoning`, `cache.read`, `cache.write`), `part.cost` (0 for a free model) | stats `GenTokens`; the last one gives the session end line with tokens and cost |
  | `retry` | `attempt`, `nextRetryAt`, `error.message` | `!` retrying line; a quota line when the text matches (rate limits show here first) |
  | `background_wait` | `jobs[].title` | nothing (the run waits for background jobs) |
  | `error` † | `error.type`, `error.message` (recorded: `provider.no-route`, "Model unavailable: opencode/no-such-model", the only line of that run; `error.name`, `error.data.message` for other provider errors *unconfirmed*) | `!` error; a quota line when the text matches |

- **Usage.** Tokens and cost per step (above).
- **Quota text.** The provider's own words, passed through: OpenAI's `insufficient_quota` / "exceeded your current
  quota" / "Rate limit reached", Anthropic's "rate_limit_error", Copilot's. Proposed `quotaPatterns`: `rate limit`,
  `rate_limit`, `usage limit`, `insufficient_quota`, `exceeded your current quota`. A local server has no quota; its
  failure is "server down", which `Settle` finds with the endpoint probe already.
- **Exit codes.** *(checked in code)* `run` sets exit code 1 when the session ends in failure, after writing an `error`
  event; otherwise 0 *(recorded: 0 and 1)*. Either way an `error` with no result line settles the run as `no result line` or `out of quota`.
- **Instructions files.** Reads `AGENTS.md`, and Claude's `CLAUDE.md` and skills unless `OPENCODE_DISABLE_CLAUDE_CODE`
  is set *(checked)*. A project with a `CLAUDE.md` written for Claude workers would reach OpenCode workers too; the
  OpenCode card decides whether its env sets `OPENCODE_DISABLE_CLAUDE_CODE=1`.
- **Windows quirks.** OpenCode needs Git for Windows (its messages say "Install Git for Windows or set
  `OPENCODE_GIT_BASH_PATH`"), so its `shell` tool most likely runs Git Bash: the recorded run's commands were POSIX
  (`cat notes.txt`, `echo two >> notes.txt`) and worked, and the prompt's PowerShell commands would then run as
  `powershell -c ...` from bash *(unconfirmed)*. `--standalone` (above) avoids a shared background service outliving
  the worker. Stdin must be closed and `PWD` set (above; AG-17, AG-19).
- **Messages (WRK-14).** OpenCode plugins (`tool.execute.after`) run after each tool, but adding text to the model's
  context from one is not confirmed. Workers read `msg-slotN.txt` themselves; `Hooks` stays false.
- **Endpoint.** With a local server, the endpoint is the config's or the worker's (WRK-4 unchanged); with a hosted
  provider, `"endpoint": ""`.

Proposed config entry (local server):

```json
"opencode": {
  "cli": "opencode",
  "format": "opencode",
  "args": ["run", "--format", "json", "--standalone", "--auto", "--archived", "-m", "local/{model}", "{prompt}"],
  "env": { "OPENCODE_CONFIG_CONTENT": "<the provider and permission JSON above, on one line>" },
  "quotaPatterns": ["rate limit", "rate_limit", "usage limit", "insufficient_quota", "exceeded your current quota"]
}
```

## 4. Rules

- **AG-1 Fit.** An agent the scheduler supports has: a one-shot mode that runs one prompt to the end and exits, with
  the prompt as one argument; a model flag; tool use approved with no question asked; one JSON event per line on
  stdout or stderr; an exit code; native Windows. Codex (`codex exec --json`) and OpenCode (`opencode run --format
  json`) both qualify *(checked from their help and code; a recorded run, AG-15, confirms it)*.
- **AG-2 Format names.** `format` in an agent's config accepts `codex` and `opencode` besides `copilot`, `claude` and
  `text` (anything else stays `text`, G-1). Without `format`, the guess (CFG-10) is `codex` when the `cli` is `codex`
  and the args hold `--json`, `opencode` when the `cli` is `opencode` and the args hold `--format json`; the existing
  guesses are unchanged.
- **AG-3 One raw log.** The raw log is what the CLI writes to stdout and stderr, in order (WRK-12 unchanged). A line not
  starting `{` is the CLI's own text and shows as `!` (as for Copilot and Claude), except Codex's fixed
  `Reading additional input from stdin...`, which every Codex run prints and which is ignored. A JSON line of an
  unknown type is ignored, unless its type contains `error` (then `!` with the type and a clip of the line). Codex's
  `item.completed` with `item.type = error` is a warning: `!`, and the run goes on.
- **AG-4 Messages and the result.** Only the model's own message text becomes `*` lines: Codex `item.completed` with
  `agent_message`; OpenCode `text` parts. Nothing else (reasoning, tool output, errors, the echoed prompt) is ever
  rendered with `* `, so TIME-14 cannot read a `LANE-RESULT` the model only quoted from a file.
- **AG-5 Tool calls.** One `>` line per tool call, `label: <what>` clipped to 80 characters: Codex
  `command_execution` (`shell: <command>`), `file_change` (`edit: <paths>`), `mcp_tool_call` (`<server>.<tool>`);
  OpenCode `tool_use` (`<tool>: <input.command or input.filePath>`). Shown once per call, when it completes.
- **AG-6 Failed tools.** `x` with the first two lines of the output, clipped to 200: Codex `command_execution` with
  `exit_code != 0` or `status = failed`; OpenCode `tool_use` with `state.status = error` (`state.error`) or `state.metadata.metadata.exit != 0` (a failed
  command is `completed` with a non-zero exit there, as recorded).
- **AG-7 Session end.** Codex `turn.completed`, OpenCode the last `step_finish`: a session-end line with the tokens (and
  cost where given). Codex `turn.failed`: `! session failed: <message>`.
- **AG-8 Quota (TIME-15 extended).** For quota detection a JSON line counts as an error line also when its type is
  `turn.failed` (Codex), `retry` (OpenCode) or `error` (both; already matched). Each agent's default `quotaPatterns`
  are those in sections 2 and 3; the config's own list replaces them. Quota marks the agent, as WT-9 says.
- **AG-9 Exit.** As today: a non-zero exit with no result line ends the run `crashed`; a result line wins over the exit
  code.
- **AG-10 Approvals.** The args make the agent ask nothing: Codex `exec` with
  `--dangerously-bypass-approvals-and-sandbox` (the developer's choice); OpenCode with `--auto` and a permission config
  that allows `edit`, `bash` and `external_directory` and denies only what AG-11 denies.
- **AG-11 No web.** With web off: Codex gets `-c web_search="disabled"`; OpenCode's permission config denies
  `webfetch`/`websearch` and the download commands of WRK-11. What an agent cannot deny (Codex's shell downloads) is
  listed in `known_gaps.md`.
- **AG-12 Own service.** Codex is own-service (WRK-4) through `"endpoint": ""` in its entry; OpenCode uses the
  endpoint unless its entry says `"endpoint": ""`.
- **AG-13 Messages to the worker.** At first neither agent gets the message hook (WRK-14): `Delivers` is false and the
  prompt tells the worker to read its message file, as for Copilot today. Codex's `PostToolUse` hook takes the same
  `additionalContext` output as Claude's, so a later card may turn it on once a recorded run shows a per-run install
  that fires (section 2, Messages).
- **AG-14 Stats.** Calls (`turn.started` / `step_start`), tools, failed tools and generated tokens (`usage.output_tokens`
  / `tokens.output`) fill `WorkerStats`; fields an agent does not report stay 0. accounting.csv is unchanged.
- **AG-15 Recorded runs.** Each agent's parsing is tested from recorded raw logs in
  `test/unit/Scheduler.Core.Tests/Launch/Fixtures/<agent>/` (section 5), never only from hand-written lines. A fixture
  that is written by hand says so in its name (`.synthetic.jsonl`).
- **AG-16 One parser per format** (recommended, 1.3): the live view and the readable log come from the same
  `LiveEvent`s, so they cannot disagree.
- **AG-17 Closed stdin.** A Codex or OpenCode worker's command line gives the CLI an empty stdin (`< nul` before the
  log redirection): `codex exec` appends piped stdin to the prompt and would wait for it, and `opencode run` was
  recorded waiting on an open stdin with no output. Copilot's and Claude's lines are unchanged (WRK-12).
- **AG-18 No shared service.** An OpenCode worker runs `opencode run --standalone`, so each worker has its own server
  process, ended with it, and none is left running after the card.
- **AG-19 Working folder by `PWD`.** An OpenCode worker's environment sets `PWD` to the worktree, because OpenCode
  takes its project folder from `PWD` before the process's own folder (recorded); a `PWD` inherited from the shell
  that started the scheduler would put the worker in the wrong folder. Setting it for every agent is harmless and
  simpler (`WorkerEnv.Build`).

## 5. Test plan

### 5.1 Fixtures

Per agent, in `test/unit/Scheduler.Core.Tests/Launch/Fixtures/<agent>/` (`codex`, `opencode`):

| File | What it is | Recorded how |
|---|---|---|
| `ok.jsonl` | a short successful run: reads a file, runs one command that fails, one that succeeds, edits a file, ends with `LANE-RESULT: done` | the ok prompt below |
| `ok.exit` | its exit code, one line | `$LASTEXITCODE` after `cmd /c` (not `echo %ERRORLEVEL%` inside it, which cmd expands before the run: the first recording wrote 0 for a failed run) |
| `fail.jsonl` / `fail.exit` | a run the CLI itself fails: a model name that does not exist | the same command with `-m no-such-model` |
| `quota.jsonl` / `quota.exit` | a run refused for quota | copied from a real run's raw log when it happens; until then `quota.synthetic.jsonl`, built by hand from the documented text in the shape of the recorded `fail.jsonl` (Codex: `error` and `turn.failed` with "You've hit your usage limit"; OpenCode: a `retry` and an `error` with OpenAI's quota words), with `quota.exit` 1 |

Each file is exactly what the scheduler's `cmd /c ... > log 2>&1` writes (stdout and stderr together). Before commit
it is **scrubbed**: user name, home and repository paths replaced with `C:\Users\dev` and `C:\work\repo`, e-mail
addresses, host names and any key or token removed (session and thread ids may stay). Fixtures are small (under
50 KB each; cut long tool output in the middle and keep the line valid JSON).

The test project has no rule that copies fixtures to the output folder. The Codex card adds one line to
`test/unit/Scheduler.Core.Tests/Scheduler.Core.Tests.csproj` (a lock it names): `<None Include="Launch/Fixtures/**"
CopyToOutputDirectory="PreserveNewest" />`.

### 5.2 How to record

Recorded 2026-11-23 on the developer's PC (the fixture folder's `README.md` lists what is there). To record again, after a CLI
update or when a fixture is missing: sign in once (`codex login`, browser, ChatGPT account, checked with
`codex login status`; OpenCode needs nothing for its own free models or a local server, else `opencode auth login`).
Then, in PowerShell (not Git Bash, whose `PWD` OpenCode would follow, AG-19), from an empty folder outside every
project, as the run edits files there:

```powershell
git init rec; cd rec; Set-Content notes.txt 'one'; git add .; git commit -qm init
$p = 'Do each step with its own separate shell command, one command per tool call, never combined. 1. Read notes.txt. 2. Run: dotnet --no-such-switch. 3. Run: git status. 4. Append the line two to notes.txt. Then end with the line LANE-RESULT: done'
# Codex (default model; add -m <model> to pick one)
cmd /c "codex exec --json --skip-git-repo-check --ephemeral --dangerously-bypass-approvals-and-sandbox -c web_search=`"disabled`" `"$p`" < nul > ..\ok.jsonl 2>&1"; Set-Content ..\ok.exit $LASTEXITCODE
cmd /c "codex exec --json --skip-git-repo-check --ephemeral -m no-such-model `"$p`" < nul > ..\fail.jsonl 2>&1"; Set-Content ..\fail.exit $LASTEXITCODE
# OpenCode: a free model of its own provider (see opencode models); for a local server add the provider block of
# section 3 to the config and use -m local/<model>
$env:PWD = (Get-Location).Path
$env:OPENCODE_CONFIG_CONTENT = '{"permission":{"edit":"allow","bash":"allow","external_directory":"allow","webfetch":"deny"}}'
cmd /c "opencode run --format json --standalone --auto --archived -m opencode/<name>-free `"$p`" < nul > ..\ok.jsonl 2>&1"; Set-Content ..\ok.exit $LASTEXITCODE
cmd /c "opencode run --format json --standalone --auto --archived -m opencode/no-such-model `"$p`" < nul > ..\fail.jsonl 2>&1"; Set-Content ..\fail.exit $LASTEXITCODE
```

The files land beside `rec` (one folder per agent: run each pair from its own empty folder). Check that `notes.txt` in
`rec` (and nowhere else) now ends with `two`, and that the ok run has one failed command; the prompt asks for one
command per call because a model that combines them hides the failure (section 2, Windows quirks). Then copy the files
into the fixture folder, scrub them (5.1: Codex logs carry the home path in each `pwsh.exe` command; the profile folder
name may differ from the account name, so search for both), check every line but Codex's stdin notice is valid JSON,
and note the CLI's version in the folder's `README.md`.

Still worth one run each, as extra fixtures if they show something new: OpenCode without `--auto` (the auto-reject
line), OpenCode with a `bash` deny for `curl` (does it reach the `shell` tool? AG-11), and a real quota error of either
agent, which replaces its `quota.synthetic.jsonl`.

### 5.3 Tests (per agent; names are the rule IDs)

- **AG-2**: config `format` names and the guess from `cli` + args (`Config/AgentsTests`).
- **AG-3, AG-4**: rendering `ok.jsonl` line by line gives `*` lines whose last `LANE-RESULT` is `done` through
  `RunResults.ResultOf(rendered: true)`; no `*` line comes from a tool's output (the prompt text echoed in an event, if
  any, is not a `*` line); a non-JSON line gives `!`.
- **AG-5, AG-6**: `ok.jsonl` gives one `>` per tool call and exactly one `x` (the `dotnet --no-such-switch`).
- **AG-7**: a session-end line with tokens; `fail.jsonl` gives a `!` line.
- **AG-8**: `QuotaHit(quota.jsonl, the agent's default patterns)` is true; `QuotaHit(ok.jsonl, ...)` and
  `QuotaHit(fail.jsonl, ...)` are false; a model message that says "rate limit" does not count.
- **AG-9**: `Settle` with `fail.exit` and no result line gives `crashed`.
- **AG-10, AG-11**: `WorkerArgs.For` with web off adds Codex's switch; OpenCode's env holds the denies; Copilot's and
  Claude's tests unchanged.
- **AG-12**: `WorkerSelection` gives Codex no endpoint and OpenCode the config's.
- **AG-13**: `MessageHook.Delivers` is false for both; the prompt's `Hooks` is false.
- **AG-17**: `WorkerCommandLine` for a Codex or OpenCode agent has `< nul` before `> "<log>" 2>&1`; for Claude and
  Copilot the line is unchanged.
- **AG-19**: `WorkerEnv.Build` sets `PWD` to the worktree, also when the scheduler's own environment has another `PWD`.
- **AG-3 (Codex)**: the `Reading additional input from stdin...` line of each Codex fixture gives no `!`; the
  `item.type = error` line of `codex/fail.jsonl` gives `!` and is not the session-failed line.
- **AG-6 (OpenCode)**: the `dotnet --no-such-switch` `tool_use` of `opencode/ok.jsonl` (status `completed`, exit 1)
  gives `x`.
- **AG-18**: `WorkerArgs.For` for OpenCode holds `--standalone` and `--auto`; a `retry` line with rate-limit text counts
  for `QuotaHit` (AG-8).
- **AG-14**: scanning `ok.jsonl` gives calls > 0, tools, one tool failure and generated tokens > 0.
- **The live and readable views agree** (AG-16): for every line of each fixture, the page's events and the readable
  log's lines carry the same marks and text.

## 6. Effect on the plan

Sections 2 and 3 keep their shape (one card per agent: launch, parse, log, live view, quota, tests from a recording),
with three additions the planner may write into the cards: the Codex card starts with the protocol refactor of 1.3
(Copilot and Claude moved behind it, their tests unchanged) and adds the fixture copy rule to the test csproj; the
OpenCode card carries its config (permissions, no-web denies, local provider) in `env`; and each card's first step is
the recording of section 5.2, without which it parks. This file does not edit the plan.

After the check against the installed CLIs (2026-11-23) the shape still holds; the details the cards take from here:
the Codex card closes stdin on its command line (AG-17, a small change in `WorkerCommandLine` for one format), uses
`-c web_search="disabled"` for no web, and writes the shell-download gap into `known_gaps.md`; the OpenCode card runs
`--standalone --auto` (AG-10, AG-18), allows `external_directory`, reads `retry` events for quota (AG-8) and decides on
`OPENCODE_DISABLE_CLAUDE_CODE`. Message delivery by hook for Codex (AG-13) is a possible later card, not part of these
two.

After the recorded runs (2026-11-23) the shape still holds, and the recording step of each card is done (the fixtures
are in the repository; a card re-records only if its CLI has changed). The cards also take: closed stdin for OpenCode
too (AG-17, so the `WorkerCommandLine` change covers both formats); `PWD` set to the worktree in the worker's
environment (AG-19, a one-line `WorkerEnv` change, best made in the Codex card for every agent); Codex's stdin notice
ignored and its `item.type = error` treated as a warning (AG-3); OpenCode's failed command read from its nested exit
code (AG-6); and, for the OpenCode card, a check that a `bash` deny reaches its `shell` tool before the AG-11 denies are
relied on.

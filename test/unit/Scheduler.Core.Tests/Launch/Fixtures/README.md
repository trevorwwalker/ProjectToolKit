# Recorded agent runs

One folder per agent format (`codex/`, `opencode/`), holding raw worker logs exactly as the scheduler writes them
(`cmd /c <cli> <args> < nul > log 2>&1`): `ok.jsonl`, `fail.jsonl`, `quota.jsonl` (or `quota.synthetic.jsonl` when written
by hand), each with its exit code in a `.exit` file, and a note of the CLI version here.

What each file must contain, how to record it and how to scrub it (no user names, home or repository paths, e-mail
addresses, host names, keys or tokens): `docs/design/tdd/scheduler/agents.md`, section 5 (rule AG-15).

Recorded 2026-11-23 on Windows 11, in a scratch git repository holding `notes.txt`, with the prompt of section 5.2
(read the file, run `dotnet --no-such-switch`, run `git status`, append `two`, end with `LANE-RESULT: done`):

| Folder | CLI | Model | `ok` | `fail` | `quota` |
|---|---|---|---|---|---|
| `codex/` | `codex-cli 0.171.0`, signed in with a ChatGPT account | the account's default | exit 0; four `command_execution` items, the `dotnet` one with `exit_code` 1; `LANE-RESULT: done` | `-m no-such-model`: exit 1; an `item.type = error` warning, then `error` and `turn.failed` ("not supported when using Codex with a ChatGPT account") | synthetic, exit 1 |
| `opencode/` | `opencode 2.4.1`, nothing signed in | `opencode/deepseek-v4.1-flash-free` (the OpenCode provider's free model) | exit 0; four `shell` `tool_use` events, the `dotnet` one `completed` with `metadata.metadata.exit` 1; `LANE-RESULT: done` | `-m opencode/no-such-model`: exit 1; one `error` line (`provider.no-route`) | synthetic, exit 1 |

Both ran with stdin closed (`< nul`) and ended on their own. The first line of every Codex file,
`Reading additional input from stdin...`, is the CLI's own text and is not JSON. Scrubbed: the home path in Codex's
`pwsh.exe` commands is `C:\Users\dev`. Session and thread ids are left as recorded. The `quota.synthetic.jsonl` files are
written by hand from the quota text in agents.md sections 2 and 3, in the shape of the recorded `fail.jsonl`; replace
each with a real raw log (renamed `quota.jsonl`) when a real quota error is seen.

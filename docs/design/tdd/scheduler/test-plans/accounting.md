# Test plan: accounting, statistics and server metrics

Rules: ACCT-1 to ACCT-9. Source: `scheduler-next/spec.md` A8. Row formatting, model keys and
statistics are Core; the CSV file, HTTP to model servers and host sampling are Host.

### Acct_Row_FormatAndHeader
- Checks: ACCT-1 · Project: Core
- Setup: run of 001-D-test-plan, category D, slot 4, result `done`, 61.7 minutes, log 12345 bytes, stop reason `over the 120 min limit`, model `opus`.
- Expected: header `finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model` on a new file; row `<ISO time, Get-Date -Format s>,001-D-test-plan,D,4,done,62,12345,"over the 120 min limit",opus`. Minutes are `[int]` of the run's age (L2812): rounded to nearest, halves to even, so 60.5 -> 60 and 61.5 -> 62 (C# `Math.Round` default gives the same; `(int)` cast would not).

### Acct_Row_QuoteOrCommaInFields
- Checks: ACCT-1 · Project: Core · **blocked on C13**
- Setup: stop reason containing `"`; card name containing `,`.
- Expected today: a broken row (L852). Expected after C13: a row that reads back to the same fields.

### Acct_ModelColumn_KeyElseWorkerElseConfig
- Checks: ACCT-2 · Project: Core
- Cases: worker model resolves to models.json key `qwen3` -> `qwen3`; no key, worker model `m` -> `m`; neither -> config model.

### Acct_ModelKey_Resolution
- Checks: ACCT-3 · Project: Core
- Setup: models.json keys `qwen3-32b` (source `Qwen/Qwen3-32B-AWQ`), `opus`; fake `/models` returning root `/models/Qwen3-32B-AWQ`.
- Cases: `opus` -> `opus` (exact); a served name whose root's last part matches the source's last part -> `qwen3-32b`; `qwen3-32b-instruct` with no server -> `qwen3-32b` (longest prefix key); `gpt` -> ''.
- Also: `/models` is fetched once in 10 minutes (fake clock).

### Acct_Startup_OldHeaderRewritten_Last48hLoaded
- Checks: ACCT-4 · Project: Host
- Setup: accounting.csv with the header without `,model` and rows from 72 h, 47 h and 1 h ago.
- Expected: header rewritten in place with `,model`, rows kept; two rows loaded into stats.

### Acct_SeedRecent_LastTenWithLogs
- Checks: ACCT-5 · Project: Host
- Setup: 12 rows; logs `010-A-<start>.log` starting before, 30 s after and 2 min after a row's finish time.
- Expected: 10 recent events, newest first; each linked to the newest log that began no later than 1 minute after it finished (the 2-minute one is not used).

### Acct_CardStats_24hByCardExclusions
- Checks: ACCT-6 · Project: Core
- Setup: runs in the last 24 h: card 010-A twice (now in done/), 020-B once (review), 030-C once (active needs-human), 040-D once (queue), 050-E with result `no log`, 060-F `out of quota`, 070-G `model server down`; one run 25 h ago.
- Expected: "4 cards (5 runs): done 1 (25%), review 1 ..., questions 1, open 1; 0.08 cards/hr" (rate = (done+review)/24); the excluded results and the old run are not counted.

### Acct_CardStats_PerModelLines_WhenTwoModels
- Checks: ACCT-6 · Project: Core
- Setup: card 010-A ran on model x then y; 020-B on x.
- Expected: 010-A counted under y only; per-model lines for x and y shown; with a single model, no per-model lines.

### Acct_ServerMetrics_SampledAndLine
- Checks: ACCT-7 · Project: Core (parse), Host (sampling)
- Setup: fixture vLLM `/metrics` text with running 2, waiting 1, KV 0.43, token counters at two samples 30 s apart, preemptions 0.
- Expected: one row in `metrics-<host>.csv` per sample; line "server 2 running, 1 waiting, KV 43 %, <T/s>, preempted 0"; each distinct metrics URL sampled once even if two slots share it.

### Acct_ServerUp_TimeoutCacheAndLogging
- Checks: ACCT-8 · Project: Host
- Setup: fake server that hangs 10 s; then answers.
- Expected: down after a 5 s timeout, a first "down" logged; cached 20 s; "up" logged on the change; no endpoint -> up without a request.

### Acct_HostLoad_Sampled
- Checks: ACCT-9 · Project: Host
- Expected: each tick has CPU %, free and total memory and threads per worker, all numeric and in range.

## Trends (card 630-A; spec TREND-1 to TREND-3)

Tests: `Scheduler.Core.Tests/Accounting/TrendsTests.cs`, `Scheduler.Web.Tests/Views/PageTrendsTests.cs`.

### Trends_Daily_PerDayAndModel
- Checks: TREND-1 · Project: Core
- Setup: runs over 20 days, two models, a card finished twice in one day, runs with no log or out of quota.
- Expected: 14 days; bars count each card once per day and model (done or review only); the runs line counts counted runs; excluded runs absent. The chart has no text beside the runs line (TREND-1: no "runs" key, no "up to n"); `Trends_Chart_BarsLineAndDayLabels` (Web) asserts `>runs<`, `up to ` and `trunskey` are absent while the model swatches and the runs polyline stay.

### Trends_HiddenUnderTwoDays
- Checks: TREND-2 · Project: Core, Web
- Setup: runs on one local day only; then on two.
- Expected: no chart, then the chart.

### Trends_ReadFromAccountingCsv_ElseLoadedFigures
- Checks: TREND-3 · Project: Core, Host
- Expected: `Trends.RunsFromRows` parses accounting.csv lines and is re-read only when the file's last write changes; an unreadable file falls back to the loaded figures.

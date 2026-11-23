# <number>-<category letter>: <short title: what goes wrong>

Category: <letter, from jobs/categories.json>
Type: bug
Plan: <the plan whose work broke, with its step or phase, if known; else "none">
Status: ready
Priority: <normal | high>
Depends on: <card or "none">
Scope: <files or folders it may change; leave blank and the planner fills it in>
Worker: <Spark | Sonnet | Opus | Fable | Haiku (no code); leave blank and the planner picks>
Time box: <minutes; blank = default>
Version: <e.g. 3.0.61, or the commit>

<!-- Save as jobs/queue/<number>-<category letter>-<slug>.md; number = the highest card number in use + 1. -->

## What happens
<What you saw. Exact text of any error.>

## What should happen
<What you expected instead.>

## Steps to reproduce
1. <Starting state: what was running, which page or window>
2. <What you did>
3. <What you saw>

How often: <every time | sometimes | once>

## Evidence
- <Log lines with their times (scheduler log, pool log, health log path)>
- <Screenshots and other files: put them in jobs/attachments/<number>/ and link them here>
- <Card or worker involved>

## Since when
<Last version where it worked, or what changed just before, if known.>

## Done when
- <The steps above give the expected result>
- <A test that fails before the fix and passes after>

## Questions

## Result

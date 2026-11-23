# <n>-L: Short title

Category: L
Status: ready | waiting: <who or what> | needs-human: <question> | review: <what to check>
Priority: high | low (optional; leave the line out for normal)
Depends on: <n>-L (or "none")
Locks: <files or functions in another category's area this card changes> (or "none")
Time box: 120 min (set by the planner; most cards finish well inside 150)
Worker: premium, or one worker by name, number or model (optional, only when the developer asks)
Plan: jobs/plans/<plan>.md (the steps this card covers)
Type: feature | bug (optional; read from the header only; a card with none is untyped)
Version: <the version a bug was seen in; bug cards only>

<!-- Type: feature or bug. A person filing a card by hand copies card-template-feature.md or
     card-template-bug.md, which prompt for what matters for each type; this file is the common header
     the planner and the workers read. Screenshots and logs go in jobs/attachments/<number>/. -->

## Task

What to do, pointing at the plan for the detail. Self-contained: a worker with no memory of other
cards must be able to do it from this, the plan and the repository.

## Done when

The checks that end the job.

## Stop and ask if

Anything specific to this job, on top of the README's stop conditions.

## Questions

(Filled in by the worker when it parks the card or sends it to review: one line per decision,
`- Q1: <question> [<choice> -> done | <choice> -> queue | <choice>]`. Answered from the page.)

## Result

Filled in by the worker: a journal line per commit, then what was verified, and a checklist of
anything left for the developer.

# Requests to the planner, one file each

A new request is a new file here named `new-<slug>.md`, never with a number of its own. One file per request means two
workers never edit the same file. The scheduler numbers it when it reaches main, as it numbers cards: in its own commit it
renames it `<n>-<slug>.md`, n one more than the highest number used here or in `answered/` (three digits at least),
sets the `#NNN` placeholder in its title line to `#<n>`, and rewrites `request new-<slug>` to `request #<n>` in every
card in `queue/` and `active/`, logging `request numbered: <project>/new-<slug> -> <n>-<slug>`. A card waiting on
it says `Status: waiting: planner (request new-<slug>)` until then. A push to main that adds a numbered request is
refused. Run by hand, with no scheduler, name it `NNN-slug.md` yourself, numbered after the highest one used here or in
`answered/`.

```markdown
# #NNN: Short title

For: 123-A (the card that is waiting, or "none")
Status: open

## What is needed
...

## A reproducible case
...
```

The planner answers in the same file, sets `Status: answered`, and sets the waiting card back to
`ready`. Answered files stay as the record.

When a request is answered and the card it was for is in `done/` (or it is for no card), it moves
to `answered/` with `git mv`, in the commit that finishes the card or answers the request. That
folder is the record; this one holds only what is still in play. Whoever closes the card moves its
answered requests: the worker, the planner, or the developer.

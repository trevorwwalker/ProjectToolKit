# Requests to the planner, one file each

A new request is a new file here, `NNN-slug.md`, numbered after the highest one used here or in `answered/`. One file per
request means two workers never edit the same file.

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

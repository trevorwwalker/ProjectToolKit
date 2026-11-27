# ProjectToolkit changelog

## 3.4.57 (2026-11-27)

### New

### Fixed

* Three requests got the same number: the scheduler numbers requests, as it numbers cards (1493-B)
* A finished card is parked as "its work is not on main" when a rebase put the same changes there (1492-B)
* Docs: requests are numbered by the scheduler (the requests README, the guide, the spec) (1494-D)

## 3.4.55 (2026-11-26)

### New
- Estimates: read accounting rows by column name (fixes the time-limit warnings reading the wrong column) (1359-A)
- Estimates: `Kind:` in the card header and the `cardKinds` setting (1356-A)
- Estimates: the `kind` column of accounting.csv, kept across a restart (1357-B)
- Estimates: the run time table and the outcomes table (1360-A)
- Estimates: time in review, from the commits of main (the stays, their file, the one backfill) (1361-B)
- Estimates: the `Kind:` line in the card templates, `cardKinds` in the kit, and the guide (1358-D)
- Estimates: nothing feeds the review stays, so "time in review" stays empty: call it from the tick (1373-B)
- Estimates: write jobs/.logs/estimates.json and serve /estimates.json (1362-B)
- Estimates: the planner brief and planner guide tell a planner to read the estimates and set the kind (1363-D)
- A card marked `Fixes-red: yes` is dispatched while main is red, and the log says when dispatching pauses and resumes (1472-A)
- `projecttoolkit.ps1 -Action new-library`: lay down a library project, with its templates and a kit test (1475-D)
- Conventions: library projects, and how a host nests one (1474-D)
- Document `Fixes-red: yes` and the pause and resume log lines: planner guide, card templates, spec (1473-D)
- Dynamic locks are on by default, and a project with them off says so (1477-B)
- Docs: locks are on by default; how to turn them off, and what a worker and a planner see (1478-D)
- publish-release.ps1 drafts a changelog section from the finished cards, the developer edits it, and it ships in the release (1485-B)

### Fixed
- Warn on the live token count ("Restart recommended"), not on a single slow process-start reading (1311-B)
- The Gantt view shows step numbers and card ids twice, and plan-local step numbers: number it as the Plans view does (1318-A)
- The Trends window shows nothing for a project on its first day, though cards have finished (1327-C)
- The Gantt view's "now": a red line on every row, out of line with each other, and "more ahead ->" over the axis (1328-C)
- The installer test fails a release when a worker's short scheduler-next call ends during it (1354-B)
- The deploy took a worker's short scheduler-next call for the new scheduler, and rolled back a healthy build (1479-B)
- The scheduler reads only "lanes", so a categories.json made from the template (key "categories") is ignored (1486-A)
- The Trends chart's time labels run into each other: make them compact (7a 8a ... 12p 1p) (1488-C)

### Changed (may need action)

### No code change (delete what users do not need)
- Estimates for planners: design (1340-D)
- Estimates: the EST rules move into the spec, as built (1364-D)
- Guide: library projects (create one, nest it into a host, update the host, run its queue) (1476-D)
- Docs: "categories" is the key, "lanes" the old name still read (1487-D)

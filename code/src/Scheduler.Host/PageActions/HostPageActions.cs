using System.Text;
using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Core.Launch;
using Scheduler.Core.Moves;
using Scheduler.Core.PageModel;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Processes;
using Scheduler.Host.Tick;

namespace Scheduler.Host.PageActions;

/// <summary>
/// Host's <see cref="IPageActions"/> (280-B): what each POST route of PAGE-9 does, as
/// <c>schedule.ps1</c> does it. A route changes the scheduler's own state (the tick's running
/// workers, state and enabled slots) or writes through a port (the state files, main through the
/// answers worktree) and asks for a tick; each returns at once so the page thread never waits
/// (PAGE-1). The rules the routes implement are named on each one. 1064-B: what a route reads of main (workers.json,
/// categories.json, a card's text, the open cards) comes from the repository's model of main once it has loaded
/// (state-cache.md MM-14), so a click that writes nothing starts no git process and one that writes costs only its
/// write chain (MM-15).
/// </summary>
public sealed class HostPageActions(PageActionParts parts) : IPageActions
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly string[] HttpPrefixes = ["http://", "https://"];

    private int _seq;
    private Func<IMainFeed?> _feed = () => IMainFeed.Shared(parts.Config.Repo, parts.Config.Remote, parts.Config.Branch);

    /// <summary>The repository's model of main (<see cref="IMainFeed.Shared"/> unless a test sets one); null reads through the CLI.</summary>
    public IMainFeed? Feed
    {
        get => _feed();
        set => _feed = () => value;
    }

    /// <summary>The id the next accepted action reports (PAGE-1).</summary>
    private int Accepted()
    {
        parts.Succeeded?.Invoke();
        return Interlocked.Increment(ref _seq);
    }

    /// <summary>PAGE-8: pause while running; nothing else changes.</summary>
    public ActionResult Pause()
    {
        if (parts.Tick.State != SchedulerState.Running)
        {
            return ActionResult.Queued(Accepted());
        }

        parts.Tick.State = SchedulerState.Paused;
        parts.Log.Write("paused from the page");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>MOVE-8: resume from any paused state, which clears the retry counts.</summary>
    public ActionResult Resume()
    {
        if (!PageResume.ActsOn(parts.Tick.State))
        {
            return ActionResult.Queued(Accepted());
        }

        parts.Tick.State = PageResume.Apply(parts.Tick.State, parts.Tick.Quota);   // WT-9: clears every agent out of quota
        parts.Tick.Retries.Clear();
        parts.TickNow();
        parts.Log.Write("resumed from the page");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>HLTH: want a health check now.</summary>
    public ActionResult HealthNow()
    {
        if (parts.Health is null)
        {
            return Refused("the health check is not set; nothing to run now");
        }

        parts.Health.CheckNow();
        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    /// <summary>PAGE-12: drain, so running cards finish and nothing new starts.</summary>
    public ActionResult Drain()
    {
        parts.Tick.State = SchedulerState.Draining;
        parts.Log.Write("draining from the page: running cards finish, nothing new starts");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>PLAN: turn the planner's pushing on or off.</summary>
    public ActionResult PlannerToggle()
    {
        if (parts.PlannerPushGet is null || parts.PlannerPushSet is null)
        {
            return Refused("the planner is not running; nothing to turn");
        }

        var push = !parts.PlannerPushGet();
        parts.PlannerPushSet(push);
        parts.TickNow();
        parts.Log.Write($"planner push turned {(push ? "on" : "off")} from the page");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>
    /// TIME-10: every running worker is asked to stop with pageStopGraceMinutes and the scheduler
    /// drains, so it exits once they are gone (PAGE-12).
    /// </summary>
    public ActionResult StopAll()
    {
        var grace = parts.Config.PageStopGraceMinutes;
        parts.Tick.State = SchedulerState.Draining;
        parts.Log.Write(
            $"stop all from the page: every worker is asked to stop, killed after {grace} min, then the scheduler exits");
        foreach (var worker in parts.Tick.Running.Values.ToList())
        {
            RequestStop(worker, "stop all from the page", grace);
        }

        return ActionResult.Queued(Accepted());
    }

    /// <summary>
    /// Card 1004-C: a pool row's Stop. Every running worker of this project is asked to stop with
    /// pageStopGraceMinutes, as Stop all asks (TIME-10), and a running project pauses so it starts no
    /// new ones; unlike Stop all it does not drain, so the pool does not exit on it (MP-12).
    /// </summary>
    public ActionResult StopWorkers()
    {
        var grace = parts.Config.PageStopGraceMinutes;
        if (parts.Tick.State == SchedulerState.Running)
        {
            parts.Tick.State = SchedulerState.Paused;
        }

        parts.Log.Write(
            $"stop from the pool page: this project's workers are asked to stop, killed after {grace} min; the project is paused and the scheduler carries on");
        foreach (var worker in parts.Tick.Running.Values.ToList())
        {
            RequestStop(worker, "stopped from the pool page", grace);
        }

        return ActionResult.Queued(Accepted());
    }

    /// <summary>PAGE-4: a tick now. The loop's own 250 ms click debounce sits in the composition.</summary>
    public ActionResult Tick()
    {
        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    /// <summary>
    /// WRK-14: the developer's message to a running worker. Whitespace is collapsed, the text cut to
    /// 2000, the line appended to msg-slotN.txt as <c>[HH:mm] text</c> and <c>&lt;&lt; you: text</c> to
    /// the worker's readable log. A worker that is not there, or cannot be messaged, writes nothing
    /// and says so (MOVE-11).
    /// </summary>
    public ActionResult Send(int slot, string message)
    {
        var text = Collapse(message, 2000);
        if (text.Length == 0)
        {
            return Refused($"message to slot {slot} not sent: nothing to send");
        }

        if (!parts.Tick.Running.TryGetValue(slot, out var worker) || !worker.Msgs)
        {
            return Refused($"message to slot {slot} not sent: no worker there that can take one");
        }

        var line = $"[{parts.Clock.Now:HH:mm}] {text}";
        Append(WorkerFiles.MessageFile(parts.Config.LogDir, slot), line);
        if (worker.Log.Length > 0)
        {
            Append(worker.Log, $"<< you: {text}");
        }

        parts.Log.Write($"message to slot {slot} ({worker.Card}): {text}");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>MOVE-2: answer a card's question, committed through 250-B's committer.</summary>
    public ActionResult Answer(string card, int question, string choice, string note)
    {
        var text = Collapse(note, 500);
        var picked = int.TryParse(choice, out var n) ? n : -1;
        return CommitCard(card, (found, main) => CardMoves.Answer(found, main, question, picked, text, parts.Clock.Now));
    }

    /// <summary>
    /// WT-C7, WT-C11, WT-P3: the Worker types panel's Save. Checks the types as the config would read
    /// them (a name that is Premium or only digits, an address without http://, a category categories.json
    /// does not know are refused), writes workers.json in the pool form with main's line endings and
    /// commits it as "scheduler: workers - types ..." - or not at all when main already says this.
    /// The tick reads it back from main and applies it next tick.
    /// 947-B: a settings change must never strand work, so the same save also moves the references to
    /// a renamed type. Each row carries the name it was loaded as, which is what tells a rename from a
    /// remove-plus-add; a rename rewrites the <c>Worker:</c> line of every open card that names the old
    /// name in the same commit as workers.json, so main never has a card naming a type that does not
    /// exist; a worker already running under the old name keeps running and is recorded under the new
    /// one, so a restart adopts it there; and a type that would leave while open cards name it, or
    /// while one of its workers is still running, is refused with those cards or workers named
    /// (947-B Q2, the developer, 2026-11-19). Setting a cap to 0 is only off, and saves even with a
    /// worker of that type running.
    /// </summary>
    public ActionResult SaveWorkerTypes(int maxWorkers, IReadOnlyList<PageWorkerTypeView> types)
    {
        // 974-B (WT-C7): the panel writes back what it shows, and until main has been read what it
        // shows is the migration of the slots - so a Save here would write a "worker" over the names
        // the file on main holds. Refused until the types have come from main.
        if (parts.WorkersFromMain?.Invoke() == false)
        {
            return Refused("worker types not saved: the scheduler has not read workers.json from main yet");
        }

        if (maxWorkers < 1 || types.Count == 0)
        {
            return Refused("worker types not saved: there must be at least one worker and one type");
        }

        var bad = types.SelectMany(t => new[] { t.Endpoint, t.MetricsUrl })
            .Where(url => url.Length > 0 && !HttpPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (bad.Count > 0)
        {
            return Refused($"worker types not saved: a server or metrics address must start with http:// or https:// ({string.Join(", ", bad)})");
        }

        var known = parts.Tick.Categories.Current?.Categories;
        var pool = new WorkerPool
        {
            MaxWorkers = maxWorkers,
            Types =
            [
                .. types.Select(t => new WorkerType
                {
                    Name = t.Name.Trim(),
                    Max = Math.Max(0, t.Max),
                    Agent = t.Agent,
                    Model = t.Model,
                    Endpoint = t.Endpoint,
                    MetricsUrl = t.MetricsUrl,
                    Premium = t.Premium,
                    Projects = t.Projects,
                }),
            ],
        };
        var model = MainRead.Fresh(Feed);
        var main = MainRead.Text(model, parts.Main, parts.WorkersRel);
        var text = WorkersJsonText.WithLineEndings(WorkerTypesJson.BuildPool(pool), main);
        if (WorkerTypesJson.Parse(text, parts.Config, parts.Log) is null)
        {
            return Refused("worker types not saved: a type name is empty, repeated, Premium or only digits");
        }

        // 947-B: the rows as the panel loaded them. A row's original name is what makes a rename a
        // rename rather than one type removed and another added; a row with none is a new type.
        var rows = types
            .Select(t => (Name: t.Name.Trim(), Original: t.OriginalName.Trim().Length > 0 ? t.OriginalName.Trim() : t.Name.Trim()))
            .ToList();
        List<(string Original, string Name)> renames = [.. rows
            .Where(r => !string.Equals(r.Original, r.Name, StringComparison.Ordinal))
            .Select(r => (r.Original, r.Name))];

        // What the types are now, which is what the panel was built from (WT-P3): the pool's names, or
        // the ones the slots migrate to while workers.json is still in the old form.
        List<string> loaded = [.. TickHost.PoolOf(parts.Tick, parts.Config).Types.Select(t => t.Name)];
        List<string> gone = [.. loaded.Where(name => !rows.Any(r => Text.Same(r.Original, name)))];

        // The cards a rename or a removal touches, read from main rather than from the card cache: the
        // commit below is made against main, and must not strand a card on a name nobody has.
        var open = renames.Count > 0 || gone.Count > 0 ? OpenCards(model) : [];

        if (gone.Count > 0)
        {
            var used = gone
                .Select(name => (Name: name, Cards: CardWorkerLine.Naming(open, name)))
                .Where(u => u.Cards.Count > 0)
                .ToList();
            // 947-B Q2 (the developer, 2026-11-19): a type whose worker is still running leaves no more
            // easily than one the cards name - the worker would finish as a worker with no type, and a
            // restart could not adopt it. Turning a type off (cap 0) with a worker running is allowed.
            var live = gone
                .Select(name => (Name: name, Workers: parts.Tick.Running.Values
                    .Where(worker => Text.Same(worker.Type, name))
                    .OrderBy(worker => worker.Slot)
                    .ToList()))
                .Where(u => u.Workers.Count > 0)
                .ToList();
            if (used.Count > 0 || live.Count > 0)
            {
                // 947-B rule 4: nothing is written; the page says which cards or workers hold it back.
                var why = used.Select(u =>
                        $"type {u.Name} is used by {string.Join(", ", u.Cards.Select(c => c.Card.Id))}"
                        + ": change their Worker: lines or keep the type")
                    .Concat(live.Select(u =>
                        $"type {u.Name} has "
                        + string.Join(", ", u.Workers.Select(w =>
                            w.Card.Length > 0 ? $"slot {w.Slot} ({w.Card})" : $"slot {w.Slot}"))
                        + $" running: stop {(u.Workers.Count == 1 ? "it" : "them")} or keep the type"));
                return Refused(string.Join("; ", why));
            }
        }

        var what = $"types {string.Join(", ", pool.Types.Select(t => $"{t.Name} {t.Max}"))} of {maxWorkers}";
        var writes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (main != text)
        {
            writes[parts.WorkersRel] = text;
        }

        // 947-B rule 2: the cards that name a renamed type move with it, in this one commit.
        var touched = new List<string>();
        foreach (var (oldName, newName) in renames)
        {
            foreach (var found in CardWorkerLine.Naming(open, oldName))
            {
                if (CardWorkerLine.Rename(found.Text, oldName, newName) is { } rewritten)
                {
                    writes[found.Card.Path] = rewritten;
                    touched.Add(found.Card.Id);
                }
            }
        }

        if (writes.Count > 0)
        {
            var result = parts.Committer.Apply(new JobsChange
            {
                Writes = writes,
                Message = $"scheduler: workers - {what}{RenameNote(renames, touched)}",
            });
            if (!result.Ok)
            {
                return Refused($"{parts.WorkersRel} not committed: {result.Error}");
            }
        }

        RetypeRunning(renames);

        parts.Log.Write($"worker types committed: {what}{RenameNote(renames, touched)}");
        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    // The commit's and the log's own words about what a rename moved: "; renamed worker to default;
    // Worker: line in 951-A, 952-B" (947-B rule 2 - the message lists the cards it changed).
    private static string RenameNote(IReadOnlyList<(string Original, string Name)> renames, IReadOnlyList<string> cards) =>
        (renames.Count > 0 ? "; renamed " + string.Join(", ", renames.Select(r => $"{r.Original} to {r.Name}")) : "")
        + (cards.Count > 0 ? $"; Worker: line in {string.Join(", ", cards)}" : "");

    /// <summary>
    /// 992-B: the Categories panel's Save, with Worker types Save's safety (947-B), so a settings change
    /// never cascades. The rows are checked first - letters one of A-Z and unique, paths and globs usable
    /// (961-A) - and refused with the reasons; overlaps between categories only warn (977-A). A category
    /// that open cards use cannot be removed or have its letter changed, and the refusal names the cards:
    /// dispatch reads a card's category from its file name, which a header rewrite cannot change, so a
    /// new letter would leave those cards in a category that no longer exists (992-B Q1, developer
    /// 2026-11-20). A letter no open card uses changes freely. The tick reads the file from main, so a new <c>owns</c> applies at once (AREA-2).
    /// </summary>
    public ActionResult SaveCategories(IReadOnlyList<PageCategory> categories, IReadOnlyList<string> addOnly)
    {
        var rows = CategoriesEdit.Normalise(categories);
        var shared = addOnly.Select(path => path.Trim()).Where(path => path.Length > 0).ToList();
        var problems = CategoriesEdit.Problems(rows, shared);
        if (problems.Count > 0)
        {
            return Refused("categories not saved: " + string.Join("; ", problems));
        }

        var model = MainRead.Fresh(Feed);
        var rel = CategoriesLocation.Rel(parts.Config.JobsDir, path => MainRead.Text(model, parts.Main, path));
        var main = MainRead.Text(model, parts.Main, rel);
        var loaded = CategoriesEdit.Parse(main)?.Categories.Keys.ToList() ?? [];

        // A row's original letter is what makes a letter change a change rather than one category
        // removed and another added; a row with none is new.
        var letters = new Dictionary<string, string>(Text.Comparer);
        foreach (var row in rows.Where(r => r.OriginalLetter.Length > 0 && !Text.Same(r.OriginalLetter, r.Letter)))
        {
            letters.TryAdd(row.OriginalLetter, row.Letter);
        }

        List<string> gone = [.. loaded.Where(letter => !rows.Any(r => Text.Same(r.OriginalLetter, letter)))];
        var open = letters.Count > 0 || gone.Count > 0 ? OpenCards(model) : [];

        var used = gone.Select(letter => (Letter: letter, Gone: true))
            .Concat(letters.Keys.Select(letter => (Letter: letter, Gone: false)))
            .Select(u => (u.Letter, u.Gone, Cards: CardCategoryLine.Using(open, u.Letter)))
            .Where(u => u.Cards.Count > 0)
            .ToList();
        if (used.Count > 0)
        {
            return Refused(string.Join("; ", used.Select(u =>
                $"category {u.Letter} is used by {string.Join(", ", u.Cards.Select(c => c.Card.Id))}: finish or move those cards first, or keep "
                + (u.Gone ? "the category" : "its letter"))));
        }

        var text = WorkersJsonText.WithLineEndings(CategoriesEdit.Text(main, rows, shared), main);
        if (CategoriesEdit.Parse(text) is not { } file)
        {
            return Refused($"categories not saved: {rel} could not be written");
        }

        var writes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (main != text)
        {
            writes[rel] = text;
        }

        var what = string.Join(", ", rows.Select(r => r.Name.Length > 0 ? $"{r.Letter} {r.Name}" : r.Letter))
            + (letters.Count > 0 ? "; letter " + string.Join(", ", letters.Select(l => $"{l.Key} to {l.Value}")) : "");
        if (writes.Count > 0)
        {
            var result = parts.Committer.Apply(new JobsChange { Writes = writes, Message = $"scheduler: categories - {what}" });
            if (!result.Ok)
            {
                return Refused($"{rel} not committed: {result.Error}");
            }
        }

        // 977-A: overlaps are allowed, and said.
        var overlaps = CategoryOverlaps.Find(file);
        if (overlaps.Count > 0)
        {
            var warning = "categories saved, but they overlap: " + string.Join("; ", overlaps);
            parts.Notice?.Invoke(warning);
            parts.Log.Write(warning);
        }

        parts.Log.Write($"categories committed: {what}");
        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    // 947-B rule 3: a worker that is already running keeps running, but it is recorded under its type's
    // new name and running.json is rewritten now rather than next tick, so a scheduler that restarts in
    // between still adopts it under the name the panel has (ADOPT-1, WT-W5). Its figures are kept.
    private void RetypeRunning(IReadOnlyList<(string Original, string Name)> renames)
    {
        if (renames.Count == 0)
        {
            return;
        }

        var byOld = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rename in renames)
        {
            byOld.TryAdd(rename.Original, rename.Name);
        }

        var changed = false;
        foreach (var worker in parts.Tick.Running.Values.ToList())
        {
            if (!byOld.TryGetValue(worker.Type, out var newName)
                || string.Equals(newName, worker.Type, StringComparison.Ordinal))
            {
                continue;
            }

            parts.Tick.Adopt(worker with { Type = newName });
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        var stats = new Dictionary<int, WorkerStats?>();
        foreach (var entry in RunningJson.Read(parts.Files.Read(StateFile.Running)))
        {
            stats[entry.Worker.Slot] = entry.Stats;
        }

        parts.Files.Write(StateFile.Running, RunningJson.Write(parts.Tick.Running.Values, slot => stats.GetValueOrDefault(slot)));
    }

    /// <summary>WT-9: one agent that ran out of quota takes cards again; the others are not touched.</summary>
    public ActionResult ResumeAgent(string agent)
    {
        if (!parts.Tick.Quota.Clear(agent))
        {
            return Refused($"agent {agent} is not out of quota");
        }

        parts.TickNow();
        parts.Log.Write($"agent {agent} resumed from the page");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>MOVE-9: open categories.json with whatever opens .md files here.</summary>
    public ActionResult OpenCategories()
    {
        // 1007-C: the file that is on disk in the project's folder, not the one main has - a checkout
        // that has not caught up with a rename still has only lanes.json.
        var jobs = parts.Config.JobsDir;
        var preferred = $"{jobs}/{CategoriesLocation.Name}";
        var older = $"{jobs}/{CategoriesLocation.OldName}";
        string OnDisk(string rel) => Path.Combine(parts.Config.Repo, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(OnDisk(preferred)))
        {
            return OpenFile(preferred);
        }

        return File.Exists(OnDisk(older))
            ? OpenFile(older)
            : Refused($"cannot open categories: neither {OnDisk(preferred)} nor {OnDisk(older)} exists");
    }

    /// <summary>MOVE-9: open workers.json with whatever opens .md files here.</summary>
    public ActionResult OpenWorkers() => OpenFile(parts.WorkersRel);

    /// <summary>
    /// LANE-3: accepting a category from the page stores its new definition as accepted, which frees the
    /// category - the next tick reads categories.json against the accepted definitions and lets its cards go.
    /// </summary>
    public ActionResult AcceptCategory(string category)
    {
        var categories = parts.Tick.Categories.Current;
        if (categories is null || !categories.Categories.TryGetValue(category, out var definition))
        {
            return Refused($"no category {category} in {CategoriesLocation.Rel(parts.Config.JobsDir, path => MainRead.Text(Feed, parts.Main, path))}");
        }

        AcceptedCategories.Store(parts.Files, category, definition.Json);
        parts.TickNow();
        parts.Log.Write($"category {category}: new definition accepted from the page");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>
    /// Roadmap item 1 (job card 580-A): Overwrite takes the toolkit's template for a generic file the
    /// project edited, in one commit through the answers worktree. The Waiting on you row goes away
    /// with the next snapshot, which reads <c>TemplateRefresh.Edited</c>.
    /// </summary>
    public ActionResult TemplateOverwrite(string project, string file)
    {
        if (TemplatesRefused(project, file, out var message))
        {
            return Refused(message);
        }

        var result = parts.Templates!.Overwrite(file);
        if (!result.Ok)
        {
            return Refused($"could not overwrite {file} with the toolkit's template: {result.Error}");
        }

        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    /// <summary>
    /// Roadmap item 1 (job card 580-A): Keep leaves the project's own copy of a generic file and stops
    /// asking about it until the toolkit's template changes again.
    /// </summary>
    public ActionResult TemplateKeep(string project, string file)
    {
        if (TemplatesRefused(project, file, out var message))
        {
            return Refused(message);
        }

        if (!parts.Templates!.Keep(file))
        {
            return Refused($"{file} is not a generic file of {project}, so there is nothing to keep");
        }

        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    // The two template routes share these checks: this scheduler keeps templates at all, the request
    // names the project it runs, and it names a file (roadmap item 1, 580-A).
    private bool TemplatesRefused(string project, string file, out string message)
    {
        if (parts.Templates is null)
        {
            message = "this scheduler keeps no templates of the toolkit, so there is nothing to overwrite or keep";
            return true;
        }

        if (!Text.Comparer.Equals(project, parts.Config.Project))
        {
            message = $"{project} is not the project this scheduler runs ({parts.Config.Project}); its templates are not kept here";
            return true;
        }

        if (file.Length == 0)
        {
            message = "no file was named";
            return true;
        }

        message = "";
        return false;
    }

    /// <summary>MOVE-9: bring a card to the front (250-B's opener).</summary>
    public ActionResult Open(string card)
    {
        var found = FindCard(card);
        if (found is null)
        {
            return Refused($"no card {card} on {parts.Config.Branch}");
        }

        if (parts.Opener is null)
        {
            return Refused($"cannot open {found.Name} here: no file launcher is set");
        }

        var error = parts.Opener.Open(found.Path);
        return error.Length == 0 ? ActionResult.Queued(Accepted()) : Refused($"open from the page failed: {error}");
    }

    /// <summary>MOVE-7: send a card back from review, or resume one that waits on the developer.</summary>
    public ActionResult ResumeCard(string card) => ResumeCard(card, "");

    /// <summary>MOVE-7 with the developer's note put in the card's <c>## Dev</c> section (1056-B).</summary>
    public ActionResult ResumeCard(string card, string note) =>
        CommitCard(card, (found, main) =>
        {
            var edit = CardMoves.Resume(found, main, parts.Clock.Now);
            var how = found.Folder == CardFolder.Review ? "sent back from review." : found.Folder == CardFolder.Queue ? "set ready." : "resumed.";
            return DevNote.Add(edit, note, how, parts.Clock.Now);
        });

    /// <summary>MOVE-6: mark a review card done.</summary>
    public ActionResult Done(string card) => Done(card, "");

    /// <summary>1099-C: MOVE-6 with the developer's note, if he typed one, in the card's <c>## Dev</c> section.</summary>
    public ActionResult Done(string card, string note) =>
        CommitCard(card, (found, main) => WithNote(CardMoves.Done(found, main, parts.Clock.Now), note, "reviewed, done."));

    /// <summary>1099-C: the note box's own button: the note goes in the card's <c>## Dev</c> section and nothing else changes.</summary>
    public ActionResult SaveNote(string card, string note) =>
        string.IsNullOrWhiteSpace(note)
            ? Refused("the note is empty, so nothing was saved")
            : CommitCard(card, (found, main) => DevNote.Add(CardMoves.Note(found, main), note, "", parts.Clock.Now));

    // 1099-C: a note typed with another button is filed with it; an empty box adds no line at all.
    private CardEdit WithNote(CardEdit edit, string note, string fallback) =>
        string.IsNullOrWhiteSpace(note) ? edit : DevNote.Add(edit, note, fallback, parts.Clock.Now);

    /// <summary>MOVE-8: clear a card's retry count so it can start again.</summary>
    public ActionResult Retry(string card, string note)
    {
        var result = Retry(card);
        return string.IsNullOrWhiteSpace(note) || !result.Ok ? result : SaveNote(card, note);
    }

    /// <summary>MOVE-8: clear a card's retry count so it can start again.</summary>
    public ActionResult Retry(string card)
    {
        // 1084-B: the page names the card by its file name (the Stuck row's), but the run counts are kept by id.
        var id = FindCard(card)?.Id ?? card;
        if (!CardMoves.Retry(parts.Tick.Retries, id) && !CardMoves.Retry(parts.Tick.Retries, card))
        {
            return ActionResult.Queued(Accepted());
        }

        parts.TickNow();
        parts.Log.Write($"retry from the page: {card}'s run count cleared");
        return ActionResult.Queued(Accepted());
    }

    /// <summary>TIME-10, WT-P2: stop one worker cleanly, with pageStopGraceMinutes; its card then waits on you (<see cref="Scheduler.Host.Processes.StoppedCardHold"/>).</summary>
    public ActionResult Stop(int slot)
    {
        if (!parts.Tick.Running.TryGetValue(slot, out var worker))
        {
            return Refused($"no worker on slot {slot} to stop");
        }

        RequestStop(worker, "stopped from the page", parts.Config.PageStopGraceMinutes);
        return ActionResult.Queued(Accepted());
    }

    /// <summary>TIME-12, WT-P2: kill a worker's whole tree and tick at once; its card then waits on you.</summary>
    public ActionResult Kill(int slot)
    {
        if (!parts.Tick.Running.TryGetValue(slot, out var worker))
        {
            return Refused($"no worker on slot {slot} to kill");
        }

        parts.Processes.EndJob(worker.Job, worker.Pid);
        parts.Tick.Adopt(worker with
        {
            KillTime = parts.Clock.Now,
            StopWhy = worker.StopWhy.Length > 0 ? worker.StopWhy : "killed from the page",
        });
        parts.TickNow();
        parts.Log.Write($"slot {slot}: killed from the page; its card then waits on you");
        return ActionResult.Queued(Accepted());
    }

    // TIME-9: a worker already asked to stop keeps its reason and deadline; a shorter grace is the
    // only thing a second request may change. TIME-10: the flag file is what tells the worker.
    private void RequestStop(RunningWorker worker, string why, int grace)
    {
        if (worker.StopAt is not null)
        {
            if (grace >= worker.StopGraceMinutes)
            {
                return;
            }

            parts.Tick.Adopt(worker with { StopGraceMinutes = grace });
            parts.Log.Write($"slot {worker.Slot} stop deadline moved to {grace} min ({why})");
            return;
        }

        parts.Tick.Adopt(worker with { StopAt = parts.Clock.Now, StopWhy = why, StopGraceMinutes = grace });
        var flag = WorkerFiles.StopFlag(parts.Config.LogDir, worker.Slot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(flag)!);
            File.WriteAllText(flag, string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            parts.Log.Write($"slot {worker.Slot}: could not write the stop file ({e.Message})");
        }

        parts.Log.Write(
            $"slot {worker.Slot} asked to stop cleanly ({worker.Card}): {why}; killed after {grace} min if still running");
    }

    // The open cards on main, from the model when it has loaded (1064-B), else through the CLI.
    private IReadOnlyList<CardWorkerLine.OpenCard> OpenCards(IMainModel? model) =>
        model is not null ? CardWorkerLine.Open(model, parts.Config.JobsDir) : CardWorkerLine.Open(parts.Main, parts.Config.JobsDir);

    private ActionResult CommitCard(string card, Func<Card, string, CardEdit> make)
    {
        var found = FindCard(card);
        if (found is null)
        {
            return Refused($"no card {card} on {parts.Config.Branch}");
        }

        CardEdit? last = null;
        CommitResult result;
        if (parts.Committer is AnswersCommitter answers)
        {
            result = answers.UpdateCard(found.Path, text => last = make(found, text));
        }
        else
        {
            var main = MainRead.Text(MainRead.Fresh(Feed), parts.Main, found.Path);
            if (main is null)
            {
                return Refused($"{found.Path} is no longer on {parts.Config.Branch}");
            }

            last = make(found, main);
            result = last.Ok
                ? parts.Committer.Apply(last.Change)
                : new CommitResult { Ok = false, Error = last.Error };
        }

        if (!result.Ok)
        {
            return Refused(result.Error);
        }

        if (last is { Ok: true } edit)
        {
            CardMoves.Committed(edit, parts.Tick.Retries);
            parts.Overlay?.Landed(edit, found);
        }

        parts.TickNow();
        return ActionResult.Queued(Accepted());
    }

    private Card? FindCard(string card) =>
        parts.Tick.Cards.FirstOrDefault(found => Text.Same(found.Name, card) || Text.Same(found.Id, card));

    private ActionResult OpenFile(string rel)
    {
        if (parts.Launcher is null)
        {
            return Refused($"cannot open {rel} here: no file launcher is set");
        }

        var file = Path.Combine(parts.Config.Repo, rel.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            parts.Launcher.Open(file);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return Refused($"open from the page failed: {e.Message}");
        }

        return ActionResult.Queued(Accepted());
    }


    private ActionResult Refused(string message)
    {
        parts.Notice?.Invoke(message);
        parts.Log.Write(message);
        return ActionResult.Failed(message);
    }

    private static void Append(string file, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, line + "\n", Utf8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file the worker is holding is not worth losing the click over.
        }
    }

    // The page's own collapsing (PAGE-9): runs of whitespace to one space, then the cut.
    private static string Collapse(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var one = string.Concat(text.Select(c => char.IsWhiteSpace(c) ? ' ' : c));
        while (one.Contains("  ", StringComparison.Ordinal))
        {
            one = one.Replace("  ", " ", StringComparison.Ordinal);
        }

        one = one.Trim();
        return one.Length > max ? one[..max] : one;
    }
}

// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// The one background worker the page's POST actions run on (957-B). A click used to run to the end
/// on its own request thread - including its git work in the answers worktree - so the page waited
/// seconds for a reply and looked dead (the developer, 2026-11-19: answers 10 s apart, a Done that
/// looked like it did nothing). Now the router validates the request, enqueues the work here and
/// answers at once; the worker takes the actions in click order and runs one at a time, so two
/// commits never overlap.
/// <para>
/// Each action carries a state - queued, running, done or failed with the scheduler's own message -
/// which <c>/live.json</c> hands to the page so the marks the click made can be replaced by what
/// really happened (PAGE-1, PAGE-3). Pure state actions (Pause, Resume, Drain, the planner toggle)
/// go through <see cref="Run"/>, which runs them on the request thread: they change in-memory state
/// only, commit nothing, and must not wait behind someone else's git work.
/// </para>
/// </summary>
public sealed class PageActionQueue : IDisposable
{
    /// <summary>How many finished actions the live feed still reports, newest last.</summary>
    public const int Keep = 50;

    private readonly object _gate = new();
    private readonly Queue<Entry> _todo = new();
    private readonly List<Entry> _seen = [];
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _worker;
    private bool _stopping;
    private bool _started;
    private int _seq;
    private bool _running;

    /// <summary>Called after an action lands (done or failed), on the worker thread. The page server
    /// uses it to note the click, so a page waiting on one refreshes when its work has really
    /// landed rather than when the click came in.</summary>
    public Action<Entry>? Landed { get; set; }

    /// <summary>Whether the worker has nothing left to do.</summary>
    public bool Idle
    {
        get
        {
            lock (_gate)
            {
                return !_running && _todo.Count == 0;
            }
        }
    }

    /// <summary>Starts the worker thread. Enqueue starts it too, so a caller that never posts is
    /// never paying for a thread.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _stopping)
            {
                return;
            }

            _started = true;
            _worker = new Thread(Loop) { IsBackground = true, Name = "scheduler-page-actions" };
        }

        _worker.Start();
    }

    /// <summary>
    /// Puts one action at the back of the queue and answers at once with its id. The work runs on
    /// the worker thread in click order, one at a time.
    /// </summary>
    /// <param name="kind">What was clicked, as the page names it (answer, done, queue, stop, ...).</param>
    /// <param name="card">The card or worker the action names, when it names one.</param>
    /// <param name="work">The action itself; it runs on the worker thread.</param>
    public int Enqueue(string kind, string? card, Func<ActionResult> work)
    {
        Entry entry;
        lock (_gate)
        {
            entry = new Entry(NextId(), kind, card, work);
            _todo.Enqueue(entry);
            Remember(entry);
        }

        Start();
        _wake.Set();
        return entry.Id;
    }

    /// <summary>
    /// Runs one action here and now, on the calling thread, and records it all the same. The fast
    /// paths use it: they only touch the in-memory state, so they land before the reply goes out and
    /// never wait behind a commit (957-B item 4).
    /// </summary>
    public int Run(string kind, string? card, Func<ActionResult> work)
    {
        Entry entry;
        lock (_gate)
        {
            entry = new Entry(NextId(), kind, card, work);
            Remember(entry);
        }

        Execute(entry);
        return entry.Id;
    }

    /// <summary>
    /// Refuses a request without running anything: the action is recorded as failed with the
    /// scheduler's reason, so the page's notice line says why the item came back.
    /// </summary>
    public int Refuse(string kind, string? card, string message)
    {
        Entry entry;
        lock (_gate)
        {
            entry = new Entry(NextId(), kind, card, null);
            entry.State = "failed";
            entry.Message = message;
            Remember(entry);
        }

        Landed?.Invoke(entry);
        return entry.Id;
    }

    /// <summary>The actions the live feed reports: the last <see cref="Keep"/>, oldest first.</summary>
    public IReadOnlyList<PageActionState> States()
    {
        lock (_gate)
        {
            return [.. _seen.Select(entry => new PageActionState(entry.Id, entry.Kind, entry.Card, entry.State, entry.Message))];
        }
    }

    /// <summary>Waits for the queue to empty. The tests use it to read what the clicks did; the page
    /// never waits here.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            // Terminal, not merely off the queue: an action the worker has just picked up is between
            // its dequeue and its state change, and a test must not read the page before it ran.
            if (Idle && States().All(state => state.State is "done" or "failed"))
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return Idle && States().All(state => state.State is "done" or "failed");
    }

    /// <summary>
    /// Stops the worker. An action already running is left to finish on its own.
    /// </summary>
    public void Dispose()
    {
        Thread? worker;
        lock (_gate)
        {
            _stopping = true;
            worker = _worker;
            _worker = null; // a second Dispose must not join the same thread again
        }

        _wake.Set();

        // The thread is only ever assigned by Start, so it is ours to join. Its IsBackground must not
        // be read here: once the loop has returned the thread is dead, and reading a property of a
        // dead thread throws ThreadStateException - which Dispose of a page whose worker had already
        // finished sometimes did (957-B).
        if (worker is not null && worker != Thread.CurrentThread)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Loop()
    {
        while (true)
        {
            Entry? next;
            lock (_gate)
            {
                if (_stopping)
                {
                    return;
                }

                next = _todo.Count > 0 ? _todo.Dequeue() : null;
                if (next is not null)
                {
                    _running = true;
                    next.State = "running";
                }
            }

            if (next is null)
            {
                _wake.WaitOne(50);
                continue;
            }

            Execute(next);

            lock (_gate)
            {
                _running = false;
            }
        }
    }

    // One action, start to finish: running, then done or failed with the scheduler's own words. An
    // action that throws must not take the worker down with it - the page would stop answering
    // clicks altogether.
    private void Execute(Entry entry)
    {
        if (entry.Work is null)
        {
            Landed?.Invoke(entry);
            return;
        }

        entry.State = "running";
        try
        {
            var result = entry.Work();
            entry.State = result.Ok ? "done" : "failed";
            entry.Message = result.Ok ? "" : result.Error;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            entry.State = "failed";
            entry.Message = e.Message;
        }

        Landed?.Invoke(entry);
    }

    // The id the next accepted action gets. Called under the gate.
    private int NextId() => ++_seq;

    // Keeps the finished actions for the live feed, newest last, and no more than Keep of them.
    private void Remember(Entry entry)
    {
        _seen.Add(entry);
        if (_seen.Count > Keep)
        {
            _seen.RemoveAt(0);
        }
    }

    /// <summary>One click: what it was, and how far it got.</summary>
    public sealed class Entry(int id, string kind, string? card, Func<ActionResult>? work)
    {
        /// <summary>The id the click was answered with.</summary>
        public int Id { get; } = id;

        /// <summary>What was clicked, as the page names it.</summary>
        public string Kind { get; } = kind;

        /// <summary>The card or worker it names, when it names one.</summary>
        public string? Card { get; } = card;

        /// <summary>The work itself; null for a request refused at the door.</summary>
        public Func<ActionResult>? Work { get; } = work;

        /// <summary>queued, running, done or failed.</summary>
        public string State { get; internal set; } = "queued";

        /// <summary>Why it failed, for the notice line.</summary>
        public string Message { get; internal set; } = "";
    }
}

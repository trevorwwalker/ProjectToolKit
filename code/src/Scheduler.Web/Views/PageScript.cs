namespace Scheduler.Web.Views;

/// <summary>
/// The script that keeps the page current without F5 (PAGE-1, PAGE-3, card 470-C), the same on
/// the single page and the combined one. Ported from the old page's script (schedule.ps1 at
/// c919b43, <c>$PageScript</c>):
/// <list type="bullet">
/// <item>Every 2 s it asks <c>/live.json</c> and updates each running worker's log, stats line,
/// minutes run and time-limit bar in place, keeping the page's scroll position. The log is a
/// scroll box (490-C): it stays pinned to the bottom while lines arrive, and where the reader
/// scrolled to until they go back down; a refresh puts each log back where it was. Each window
/// keeps its own history of at most 500 numbered lines and appends what the feed sends it
/// (610-C): it asks for the lines after the one it has reached, and a new run in the slot clears
/// it.</item>
/// <item>When the reply's <c>page</c> version differs from the page's own
/// (<c>meta[name=page-version]</c>), it fetches <c>/</c> and replaces the top bar and the desk,
/// carrying typed text, the caret and the scroll position across - but not while the user is
/// typing in a box, has a dialog open, has text selected or is reading a button's help. The swap
/// keeps each worker's log box as it is (card 965-C): the live element is moved into the new
/// markup, so its lines, its height and its scroll survive and the window below does not jump;
/// only a window that is new gets the server's box, and it is filled from the lines already
/// held. The server draws no stats for a running worker, so what each slot last really showed is
/// put back too, and an update with nothing for a slot never blanks its numbers (card 1092-C).</item>
/// <item>A button is sent with fetch (PAGE-1: 202 <c>{"id":n}</c>); it reads "Working..." until
/// the live view says the page snapshot shows the click (<c>pageDone</c>), then the page
/// refreshes. After a minute without that, it reloads.</item>
/// <item>The <b>Check main</b> button follows the live feed's <c>checking</c> (card 930-C): while a
/// health check runs it is pressed, reads "Checking &lt;sha&gt;..." and is disabled, and it goes
/// back to "Check main" when the check ends - all without a reload. A click puts it in that state
/// at once, before the next poll.</item>
/// <item>When <c>/live.json</c> does not answer, the top bar says "scheduler not responding",
/// and it keeps asking.</item>
/// <item>The appearance selector applies and keeps the chosen theme.</item>
/// </list>
/// </summary>
public static class PageScript
{
    /// <summary>The script, as it goes at the end of the body.</summary>
    public const string Html = """
        <script>
        (function () {
          var EVERY = 2000, FAST = 250;
          // 966-B: fast mode - for LIVELY ms after any click or key the live view is asked for every INTERACT ms.
          var INTERACT = 33, LIVELY = 15000, lively = 0;
          var page = version(document);
          // VW-20: on the Plans view the page is fetched again when the views version moves.
          function viewsOf(d) { var m = d.querySelector('meta[name=views-version]'); return m ? m.getAttribute('content') : ''; }
          var viewsNow = viewsOf(document);
          var timer = 0, asked = 0, liveBusy = false, pageBusy = false, pageAgain = false;
          var submitting = false, waitFor = 0, waitFrom = 0, waitSince = 0;
          // 957-B item 2: the changes the page has already made on the developer's behalf, waiting for
          // the live feed to say the scheduler really did them. Each entry is one click: what it was,
          // the card or worker it named, the id the queue answered with, and when it was clicked.
          var pending = [];
          function version(d) { var m = d.querySelector('meta[name=page-version]'); return m ? m.getAttribute('content') : ''; }
          function bySlot(sel, s) { return document.querySelector(sel + '[data-slot="' + s + '"]'); }
          function setText(n, t) { if (n && n.textContent !== t) n.textContent = t; }
          // The log is a scroll box (490-C): a refresh keeps each log where the reader had it -
          // pinned to the bottom, or where they scrolled up to read - instead of yanking them all.
          var logPlace = {};
          function noteLogs() {
            document.querySelectorAll('pre.log[data-slot]').forEach(function (p) {
              var s = p.getAttribute('data-slot');
              logPlace[s] = { top: p.scrollTop, end: p.scrollTop + p.clientHeight >= p.scrollHeight - 4 };
            });
          }
          function pinLogScroll() {
            document.querySelectorAll('pre.log[data-slot]').forEach(function (p) {
              var k = logPlace[p.getAttribute('data-slot')];
              p.scrollTop = (!k || k.end) ? p.scrollHeight : k.top;
            });
          }
          // The model and the model server's address come off the stats line (490-C, 971-C), as the
          // server side trims the rendered one: the title bar already names the model, and the
          // address is only shown in the Worker types panel, where it is set. The model to trim
          // comes from the stats div itself, which the server draws with data-model.
          function statsBits(t, model) {
            var ps = String(t || '').split(/\s*\|\s*/).filter(function (p) { return p; }), end = ps.length;
            while (end > 0) {
              var p = ps[end - 1];
              if (p.indexOf('://') < 0 && !(model && p === model)) break;
              end--;
            }
            return { stats: ps.slice(0, end).join('  |  ') };
          }
          function down(on) {
            var n = document.getElementById('live-state');
            if (!n) return;
            setText(n, on ? 'scheduler not responding' : '');
            n.classList.toggle('bad', on);
          }
          // The Check main button shows the health check while it runs (930-C): pressed, as Pause and
          // Planner push look, reading "Checking <sha>..." and disabled so it cannot be pressed twice.
          // The live feed's "checking" is the state the title bar's health line already carries, so the
          // button keeps up without a reload; a click puts it in that state at once, before the next poll.
          function healthButton(text, on) {
            // Only the pressable button - the one in a form. A remote viewer's button is disabled by
            // the server and has no form, so the script leaves it as drawn.
            var b = document.getElementById('btn-health');
            if (!b || !b.closest || !b.closest('form')) return;
            if (b.textContent !== text) b.textContent = text;
            b.classList.toggle('pressed', !!on);
            b.disabled = !!on;
          }
          function get(url, ms, opts) {
            opts = opts || {};
            opts.cache = 'no-store';
            var ac = window.AbortController ? new AbortController() : null;
            if (ac) opts.signal = ac.signal;
            var tm = setTimeout(function () { if (ac) ac.abort(); }, ms);
            return fetch(url, opts).then(function (r) {
              clearTimeout(tm);
              if (!r.ok) throw new Error('HTTP ' + r.status);
              return r;
            }, function (e) { clearTimeout(tm); throw e; });
          }
          // Each worker's window keeps its own history (610-C, PAGE-3): the feed numbers a run's
          // rendered lines from its start, the page asks for what it has not seen (?since=4:1234,1:87)
          // and appends what arrives, holding at most KEEP lines and dropping the oldest. The line
          // being written is shown after them but never kept, so it costs no place in the 500; a new
          // run in the slot clears the window, and a page opened later is filled with the last 500.
          var KEEP = 500;
          var hist = {};
          function slotHist(s) { return hist[s] || (hist[s] = { run: '', n: 0, lines: [] }); }
          function sinceParam() {
            var parts = [];
            Object.keys(hist).forEach(function (s) { if (hist[s].n > 0) parts.push(s + ':' + hist[s].n); });
            return parts.length ? '?since=' + parts.join(',') : '';
          }
          function logText(s, v) {
            var h = slotHist(s), run = v.run || '', got = v.log || [], known = !!h.run, cleared = false;
            if (run && h.run !== run) {
              // The slot started another run: its lines number from 1, so the window and the cursor
              // go. Seen for the first time, this answer is the whole window; after a run was known
              // the answer was sliced by the old cursor, so it is dropped and the next ask carries
              // none - and its number is not taken either, or the new run's first lines are missed.
              h.run = run; h.n = 0; h.lines = [];
              cleared = true;
              if (!known) { h.lines = got.slice(); cleared = false; }
            } else if (got.length) {
              // The feed could not go back as far as the cursor: it says where its window really
              // starts, and the page replaces rather than leaving a hole. Otherwise: append.
              if (typeof v.from === 'number' && v.from > h.n + 1) h.lines = got.slice();
              else h.lines = h.lines.concat(got);
            }
            if (!cleared && typeof v.next === 'number' && v.next > h.n) h.n = v.next;
            if (h.lines.length > KEEP) h.lines = h.lines.slice(-KEEP);
            var text = h.lines.slice();
            if (v.partial) text.push(v.partial);
            if (!text.length) text.push('(no output yet)');
            return text.join('\n');
          }
          // 1092-C: what each slot last really showed, kept per run. The live feed can answer without a
          // worker's stats line (its readable log was re-read, or its run has not been sampled yet), and
          // the page the server draws has none at all: the host fills no stats for a running worker, so
          // every desk swap left each window with an empty stats line and a '- T/s' until the next poll.
          // A value on screen is therefore only ever replaced by a newer real one, and the dash is drawn
          // only for a run that has not had stats yet - a new worker.
          var held = {};
          function holdOf(s) { return held[s] || (held[s] = { run: '', stats: '', tps: '', time: '' }); }
          // One running worker's window, in place: its log (kept at the bottom if it was there),
          // its stats line, its minutes run, and its time-limit bar when the time says 'n/m min'.
          // 1093-C: a window whose slot moved to another card keeps nothing of the card before it:
          // its lines, its stats line, its average and its minutes all belonged to the worker that
          // ended. The box says what is starting until this card's own worker has written.
          function startingText(card) { return '(starting ' + card + '...)'; }
          function clearSlot(s, card) {
            var h = hist[s];
            if (h) { h.run = ''; h.n = 0; h.lines = []; }
            var k = held[s];
            if (k) { k.run = ''; k.stats = ''; k.tps = ''; k.time = ''; }
            setText(bySlot('pre.log', s), startingText(card));
            setText(bySlot('.stats', s), '');
            setText(bySlot('.tag', s), '');
            setText(document.querySelector('.mini[data-slot="' + s + '"] .tps'), '- T/s');
          }
          function applySlot(s, v) {
            if (v.starting) {
              // The feed is still holding the worker that ran before this window's card: its lines are
              // another card's, so they are not drawn (1093-C).
              clearSlot(s, v.starting);
              return;
            }
            var p = bySlot('pre.log', s), text = logText(s, v);
            if (p && p.textContent !== text) {
              var atEnd = p.scrollTop + p.clientHeight >= p.scrollHeight - 4;
              p.textContent = text;
              if (atEnd) p.scrollTop = p.scrollHeight;
            }
            var k = holdOf(s), run = v.run || '';
            if (run && run !== k.run) {
              // Another worker took the slot: nothing of the last one's is kept, so its window starts
              // with the dash and the empty stats line a worker that has never had stats gets.
              k.run = run; k.stats = ''; k.tps = ''; k.time = '';
            }
            var st = bySlot('.stats', s);
            var bits = statsBits(v.stats || '', st ? st.getAttribute('data-model') || '' : '');
            if (bits.stats) k.stats = bits.stats;
            setText(st, k.stats);
            applyMini(s, k);
            if (v.time) k.time = v.time;
            setText(bySlot('.tag', s), k.time);
            // A quiet worker's spinner stops (510-B, as the old page: the time says ', quiet q min').
            var mi = bySlot('.mini', s);
            if (mi) mi.classList.toggle('idle', /quiet/.test(k.time));
            var pb = bySlot('.prog', s), m = /(\d+)\s*\/\s*(\d+) min/.exec(k.time);
            if (pb && m && +m[2] > 0) {
              var pct = Math.min(100, Math.round(100 * m[1] / m[2])), w = pct + '%';
              if (pb.style.width !== w) pb.style.width = w;
              pb.classList.toggle('late', pct >= 90);
            }
          }
          // Each worker's window has two sizes, kept per worker in this browser (card 480-C, as the
          // old page did): full (the window with its log) and mini (the title bar alone: the card, a
          // spinner, the average T/s, the minutes).
          var modeMem = {}, nextMode = { full: 'mini', mini: 'full' };
          var modeLabel = { full: 'Minimize', mini: 'Expand' };
          var modeHelp = { full: 'Show only the title bar', mini: "Show this worker's window" };
          // 1024-C: a pool page worker window (it carries data-project) starts minimized until the
          // developer picks a size; the single page's windows start full as before.
          function winDefault(b) {
            var w = b && b.closest ? b.closest('section.win') : null;
            return w && w.hasAttribute('data-project') ? 'mini' : 'full';
          }
          function logMode(s, def) {
            var v = null;
            try { v = localStorage.getItem('log-mode-' + s); } catch (x) { v = modeMem[s] || null; }
            return v === 'mini' || v === 'full' ? v : (def || 'full');
          }
          function setLogMode(s, v) { modeMem[s] = v; try { localStorage.setItem('log-mode-' + s, v); } catch (x) {} }
          function applyLogs() {
            document.querySelectorAll('button.logtoggle[data-slot]').forEach(function (b) {
              var s = b.getAttribute('data-slot'), m = logMode(s, winDefault(b)), win =b.closest ? b.closest('section.win') : null;
              if (win) { if (m === 'mini') win.classList.add('mini'); else win.classList.remove('mini'); }
              if (b.textContent !== modeLabel[m]) { b.textContent = modeLabel[m]; b.title = modeHelp[m]; }
            });
          }
          // The mini title bar's number, from the worker's stats line: the average this page last really
          // saw for this run (1092-C), so an update with no stats cannot blank it; the dash is only for a
          // worker that has had no stats at all yet.
          function applyMini(s, k) {
            var t = document.querySelector('.mini[data-slot="' + s + '"] .tps'), m = /([\d.]+) avg/.exec(k.stats || '');
            if (m) k.tps = m[1] + ' T/s avg';
            setText(t, k.tps || '- T/s');
          }
          // 1092-C: after the desk is replaced, the markup the server drew holds no stats for a running
          // worker, so each window is filled back from what this page held - the carrying-across
          // keepBoxes does for the log boxes, and it runs after them so a window gets its lines and its
          // numbers in the same pass.
          function keepStats() {
            Object.keys(held).forEach(function (s) {
              var k = held[s];
              if (k.stats) setText(bySlot('.stats', s), k.stats);
              if (k.tps) setText(document.querySelector('.mini[data-slot="' + s + '"] .tps'), k.tps);
              if (k.time) setText(bySlot('.tag', s), k.time);
            });
          }
          // Card 991-C: the Worker types panel is hidden unless this browser stored it as open. Hiding
          // only hides: the fields stay in the form, so unsaved edits survive.
          function wtOpen(k) { try { return localStorage.getItem(k) === 'open'; } catch (x) { return !!modeMem[k]; } }
          function applyWt() {
            document.querySelectorAll('button.wttoggle').forEach(function (b) {
              var f = b.closest ? b.closest('form') : null, open = wtOpen(b.getAttribute('data-key'));
              if (f) { if (open) f.classList.remove('wthidden'); else f.classList.add('wthidden'); }
              var a = b.querySelector('.wtarrow'); if (a) a.textContent = open ? '▾' : '▸';
            });
          }
          // 1123-C: a Plans node the developer opened or closed stays so across a refresh and a reload. Each
          // <details> carries a data-key; a click on its summary is remembered in sessionStorage under it, and
          // applyPlans puts the remembered state over the server's default. An untouched node follows the default.
          var planMem = {};
          function planGet(k) { try { return sessionStorage.getItem('plans-open:' + k); } catch (x) { return planMem[k] || null; } }
          function planSet(k, v) { try { sessionStorage.setItem('plans-open:' + k, v); } catch (x) { planMem[k] = v; } }
          function applyPlans() {
            document.querySelectorAll('details[data-key]').forEach(function (d) {
              var v = planGet(d.getAttribute('data-key'));
              if (v === '1' && !d.open) d.open = true;
              else if (v === '0' && d.open) d.open = false;
            });
          }
          document.addEventListener('click', function (e) {
            var t = e.target, s = t && t.closest ? t.closest('details[data-key] > summary') : null;
            if (!s || (t.closest && t.closest('a'))) return;
            var d = s.parentNode;
            planSet(d.getAttribute('data-key'), d.open ? '0' : '1');
          });
          // WT-P3: the Worker types panel's Add type copies the blank row the panel carries in a
          // template, and Remove takes one out before Save posts what is left. What a worker keeps
          // is its type, so nothing here needs a slot.
          document.addEventListener('click', function (e) {
            var c = e.target.closest ? e.target : null;
            if (!c) return;
            var a = c.closest('button.addtype');
            if (a) {
              var t = document.getElementById(a.getAttribute('data-tpl') || 'wtype-new');
              var tb = a.form ? a.form.querySelector('table.wtypes') : null;
              if (t && t.content && tb) { tb.appendChild(document.importNode(t.content, true)); }
              return;
            }
            var r = c.closest('button.typeremove');
            if (r) { var row = r.closest ? r.closest('tr') : null; if (row && row.parentNode) row.parentNode.removeChild(row); return; }
            var w = c.closest('button.wttoggle');
            if (w) {
              var wk = w.getAttribute('data-key'), nv = wtOpen(wk) ? 'closed' : 'open';
              modeMem[wk] = nv === 'open'; try { localStorage.setItem(wk, nv); } catch (x) {}
              applyWt();
              return;
            }
            var b = c.closest('button.logtoggle');
            if (!b) return;
            var s = b.getAttribute('data-slot');
            setLogMode(s, nextMode[logMode(s, winDefault(b))] || 'full');
            applyLogs();
          });
          // The "2 of 2" beside a cap follows the cap field as it is typed (WT-P3).
          document.addEventListener('input', function (e) {
            var i = e.target;
            if (!i || i.name !== 'wmax') return;
            var row = i.closest ? i.closest('tr') : null;
            var shown = row ? row.querySelector('.wrunning') : null;
            var m = shown ? /^(\d+)\s*of/.exec(shown.textContent || '') : null;
            if (m) shown.textContent = m[1] + ' of ' + (i.value || '0');
          });
          // The user is in the middle of something a refresh would undo.
          function userBusy() {
            // 1004-C: the pool page's project dropdown, like the theme menu, is not rebuilt while it is open.
            if (document.querySelector('dialog[open], .g:hover, pre.log:hover, #rangesel:focus, #themesel:focus, #projsel:focus')) return true;
            if (document.querySelector('input.wsbox:focus')) return true;   // 1033-C: a box being typed in
            var a = document.activeElement;
            if (a && (a.tagName === 'TEXTAREA' || (a.tagName === 'INPUT' && a.type === 'text')) && a.value) return true;
            return !!(window.getSelection && String(window.getSelection()));
          }
          function formField(f, name) { var h = f.querySelector('input[name=' + name + ']'); return h ? h.value : ''; }
          function noteKey(project, card) { return 'note|' + project + '|' + cardOf(card); }
          var noteBack = {};   // 1099-C: notes whose post failed, put back in their box by the next refresh
          function boxes() {
            return Array.prototype.filter.call(document.querySelectorAll('#desk input[name], #desk textarea[name]'), function (i) {
              return i.tagName === 'TEXTAREA' || i.type === 'text';
            });
          }
          // The Worker types panel's rows share one name each, so a field is keyed by its place
          // among them too; without that a refresh writes one row's answer into every row (WT-P3).
          function keyOf(i) {
            var k = (i.form ? i.form.getAttribute('action') : '') + '|' + i.name;
            if (!i.form || !i.name) return k;
            // 1099-C: a card's note box is its own, keyed by its project and card, never by its place among the others.
            if (i.name === 'note') return noteKey(formField(i.form, 'project'), formField(i.form, 'card'));
            var sibs = i.form.querySelectorAll('[name="' + i.name + '"]');
            for (var n = 0; n < sibs.length; n++) { if (sibs[n] === i) return k + '|' + n; }
            return k + '|0';
          }
          function syncTheme() {
            var sel = document.getElementById('themesel'), k = window.storedTheme ? window.storedTheme() : null;
            if (sel && k && window.setTheme && window.setTheme(k)) sel.value = k;
          }
          // 1027-C: a refresh replaces the top bar, whose theme menu lists the themes the server has now;
          // the themes' style blocks live in the head, which a refresh used to keep. A theme added since the
          // page was loaded (new theme files, a project's own theme once it joins) was in the menu with no
          // style block, so choosing it did nothing. Take the fetched page's blocks when they differ, keep
          // the theme on show, and leave the common style after them.
          function syncThemeStyles(d) {
            var fresh = d.querySelectorAll('style[data-theme]'), cur = document.querySelectorAll('style[data-theme]');
            if (!fresh.length || !cur.length) return;
            var sig = function (list) {
              var s = '';
              for (var i = 0; i < list.length; i++) s += list[i].getAttribute('data-theme') + '\u0001' + list[i].textContent + '\u0002';
              return s;
            };
            if (sig(fresh) === sig(cur)) return;
            var on = null;
            for (var j = 0; j < cur.length; j++) if (cur[j].media === 'all') on = cur[j].getAttribute('data-theme');
            var at = cur[0];
            for (var n = 0; n < fresh.length; n++) {
              var s = document.importNode(fresh[n], true);
              s.media = 'not all';
              at.parentNode.insertBefore(s, at);
            }
            for (var m = 0; m < cur.length; m++) cur[m].remove();
            var all = document.querySelectorAll('style[data-theme]');
            if (!(on && window.setTheme && window.setTheme(on)) && all.length) all[0].media = 'all';
          }
          // 965-C: the live feed owns a worker's log box. The page the server draws carries an empty
          // one (the host fills no LogTail for a running worker), so replacing a section used to
          // empty every window and leave the next poll to refill it - the window collapsed to
          // nothing and grew back on each refresh. The swap therefore goes round the boxes: what the
          // page has is collected first, and put back into the new markup afterwards.
          function liveBoxes() {
            var live = {};
            document.querySelectorAll('pre.log[data-slot]').forEach(function (p) { live[p.getAttribute('data-slot')] = p; });
            return live;
          }
          // Each box the page already had is moved into the new markup - the same element, so its
          // lines, its height and its scroll survive - and the box the server drew is the one that
          // goes. Only a window that is new keeps the server's box, and that one is filled from the
          // lines this page already holds for the run, so it is never blank waiting on the next poll.
          function windowOf(n) {
            var w = n.closest ? n.closest('section.win') : null;
            return w ? (w.getAttribute('data-card') || '') : '';
          }
          function keepBoxes(live) {
            document.querySelectorAll('pre.log[data-slot]').forEach(function (q) {
              var s = q.getAttribute('data-slot'), old = live[s];
              if (!old || old === q || !q.parentNode) return;
              if (windowOf(old) !== windowOf(q)) {
                // Another card took this slot while the page was being fetched: that is another run,
                // so the window starts afresh and the next ask carries no cursor (610-C). 1093-C: the
                // card before loses its numbers too, so nothing of it waits under the new title for
                // the next poll to clear it.
                clearSlot(s, windowOf(q));
                return;
              }
              var cls = q.className;
              q.parentNode.replaceChild(old, q);
              old.setAttribute('data-slot', s);
              if (cls) old.className = cls;
            });
            document.querySelectorAll('pre.log[data-slot]').forEach(function (q) {
              if (q.textContent) return;
              var h = hist[q.getAttribute('data-slot')];
              if (h && h.lines.length) q.textContent = h.lines.join('\n');
            });
          }
          // ---- The pending mask (957-B item 2) ----------------------------------------------
          // A click's git work now runs on the page action queue behind the reply, so its seconds are
          // hidden: the page applies the click's own result at once, as if it had landed, and keeps it
          // applied until the live feed says the action really did. A refresh rebuilds the desk from the
          // scheduler's (older) view, so every pending change is re-applied after each one; when an
          // action lands its mark is dropped, and when it fails the item comes back with the
          // scheduler's own reason in the notice line rather than silently reappearing.
          function cardOf(v) { var m = /^(\d+-[A-Za-z])/.exec(String(v || '')); return m ? m[1] : String(v || ''); }
          // A pool's rows carry their project (MP-P3), so a click in one project leaves another's same card alone.
          function ofProject(r, project) { return !project || r.getAttribute('data-project') === project; }
          function rowsIn(col, card, project) {
            return Array.prototype.slice.call(document.querySelectorAll(col + ' tr[data-card]'))
              .filter(function (r) { return r.getAttribute('data-card') === card && ofProject(r, project); });
          }
          // What a form's click will do to the page, or null when the page cannot guess it.
          function maskOf(action, body) {
            var kinds = { '/note': 'note', '/answer': 'answer', '/done': 'done', '/resume-card': 'queue', '/retry': 'queue', '/stop': 'stop', '/kill': 'stop' };
            if (action === '/workspaces') {
              return { id: 0, kind: 'workspaces', project: body.get('project') || '', value: String(body.get('workspaces') || ''), at: Date.now() };
            }
            var kind = kinds[action];
            if (!kind) { return null; }
            var project = body.get('project') || '', slot = body.get('slot');
            // A pool names its workers project:slot (WT-W3), as the live view keys them.
            if (project && slot) { slot = project + ':' + slot; }
            return { id: 0, kind: kind, project: project, card: cardOf(body.get('card')), q: body.get('q'), slot: slot, note: body.get('note') || '', at: Date.now() };
          }
          // The rows that show one card in the waiting window: its own row, and the question row that
          // carries its answer form.
          function waitingRows(card, project) {
            var out = rowsIn('#w-waiting', card, project);
            document.querySelectorAll('#w-waiting tr.qrow').forEach(function (r) {
              var c = r.querySelector('input[name=card]');
              if (c && cardOf(c.value) === card && ofProject(r, project)) { out.push(r); }
            });
            return out;
          }
          // Put every pending change back over the page as it now stands. Called after each refresh,
          // which rebuilds the desk from the scheduler's view and would otherwise bring back what the
          // developer has already clicked away.
          function applyPending() {
            pending.forEach(function (p) {
              if (p.kind === 'workspaces') {
                // 1033-C: the box shows the posted value until a snapshot carries it.
                var wb = wsBox(p.project);
                if (wb && document.activeElement !== wb) { wb.value = p.value; wb.classList.add('pend'); wb.style.opacity = '0.6'; }
              } else if (p.kind === 'answer') {
                document.querySelectorAll('#w-waiting tr.qrow').forEach(function (r) {
                  var c = r.querySelector('input[name=card]'), q = r.querySelector('input[name=q]');
                  if (c && q && cardOf(c.value) === p.card && String(q.value) === String(p.q) && ofProject(r, p.project)) { r.style.display = 'none'; }
                });
                // The card's row goes when its last open question is answered.
                var open = waitingRows(p.card, p.project).some(function (r) { return r.classList.contains('qrow') && r.style.display !== 'none'; });
                if (!open) { rowsIn('#w-waiting', p.card, p.project).forEach(function (r) { r.style.display = 'none'; }); }
              } else if (p.kind === 'done' || p.kind === 'queue') {
                waitingRows(p.card, p.project).forEach(function (r) { r.style.display = 'none'; });
                if (p.kind === 'queue') {
                  rowsIn('#col-queue', p.card, p.project).forEach(function (r) {
                    r.classList.add('resuming');
                    var cell = r.querySelector('td');
                    if (cell && !cell.querySelector('.resuming-tag')) {
                      var tag = document.createElement('span');
                      tag.className = 'resuming-tag';
                      tag.textContent = ' resuming';
                      cell.appendChild(tag);
                    }
                  });
                }
              } else if (p.kind === 'stop') {
                var log = bySlot('pre.log', p.slot), win = log && log.closest ? log.closest('section.win') : null;
                if (win) { win.classList.add('stopped'); win.style.opacity = '0.5'; }
              }
            });
          }
          // Replace the pending marks with what the queue reports: an action that landed is no longer
          // pending (the next refresh shows the truth), and one that failed is taken back off the mask
          // so the item reappears with the scheduler's reason in the notice line.
          function settlePending(actions) {
            if (!pending.length) { return; }
            for (var i = pending.length - 1; i >= 0; i--) {
              var p = pending[i], st = null;
              for (var k = 0; k < actions.length; k++) { if (actions[k].id === p.id) { st = actions[k]; break; } }
              if (!st) { if (Date.now() - p.at > 120000) { pending.splice(i, 1); } continue; }
              // 990-B: landed is not shown yet - the mark stays until a page fetched after this agrees
              // (agreePending); a Stop's window is the worker's own, so its mark goes when it lands.
              if (st.state === 'done') {
                if (!p.landed) { p.landed = Date.now(); }
                if (p.kind === 'stop' || Date.now() - p.landed > 60000) { pending.splice(i, 1); }
              }
              else if (st.state === 'failed') {
                pending.splice(i, 1);
                if (p.note) { noteBack[noteKey(p.project, p.card)] = p.note; }
                sayNotice((st.card ? st.card + ': ' : '') + (st.msg || 'that did not happen'));
                refresh(true);
              }
            }
          }
          // 990-B: whether the page as it now stands still shows what a pending click took away.
          function wsBox(project) {
            var row = Array.prototype.slice.call(document.querySelectorAll('table.projects tr[data-project]'))
              .filter(function (r) { return r.getAttribute('data-project') === project; })[0];
            return row ? row.querySelector('input.wsbox') : null;
          }
          // 1033-C: a number box posts when changed (Enter or leaving it). Not a whole number 1..64: refused
          // here with a notice naming the project, and the box goes back to the stored value.
          document.addEventListener('change', function (e) {
            var box = e.target;
            if (!box || !box.classList || !box.classList.contains('wsbox')) return;
            var f = box.form, project = f.querySelector('input[name=project]').value, v = String(box.value).trim();
            var n = /^\d+$/.test(v) ? parseInt(v, 10) : 0;
            if (n < 1 || n > 64) {
              box.value = f.getAttribute('data-stored');
              sayNotice(project + ': workspaces not saved: ' + (v || 'nothing') + ' is not a whole number 1 to 64');
              return;
            }
            var busy = parseInt(f.getAttribute('data-busy'), 10) || 0;
            if (n < busy) { sayNotice(project + ': ' + busy + ' workers are running; they finish in place, and no new one starts until fewer than ' + n + ' are busy'); }
            if (f.requestSubmit) { f.requestSubmit(); } else { f.submit(); }
          });
          function showsPending(p) {
            if (p.kind === 'workspaces') {
              var wb = wsBox(p.project), fm = wb ? wb.form : null;
              return !!fm && String(fm.getAttribute('data-stored')) !== p.value;
            }
            if (p.kind === 'answer') {
              return Array.prototype.slice.call(document.querySelectorAll('#w-waiting tr.qrow')).some(function (r) {
                var c = r.querySelector('input[name=card]'), q = r.querySelector('input[name=q]');
                return !!(c && q && cardOf(c.value) === p.card && String(q.value) === String(p.q) && ofProject(r, p.project));
              });
            }
            return waitingRows(p.card, p.project).length > 0;
          }
          // 990-B: a landed click's mark is dropped only when a page fetched after it landed no longer
          // shows the item: the scheduler's view can trail the action, and dropping the mark on
          // "landed" alone brought the answered or finished card back until its next card read.
          function agreePending(since) {
            for (var i = pending.length - 1; i >= 0; i--) {
              var p = pending[i];
              if (p.landed && p.landed <= since && !showsPending(p)) { pending.splice(i, 1); }
            }
          }
          // The notice line at the top of the page; made when the scheduler had nothing to say there.
          function sayNotice(text) {
            var n = document.querySelector('.notice'), top = document.getElementById('col-top');
            if (!n && top) { n = document.createElement('div'); n.className = 'notice'; top.insertBefore(n, top.firstChild); }
            if (n) { n.textContent = text; }
          }
          // Replace the top bar and the desk with the page as it is now. force: a click's result,
          // shown whatever the user is doing.
          function refresh(force) {
            if (pageBusy) { if (force) pageAgain = true; return; }
            if (!force && (submitting || document.hidden || userBusy())) return;
            var kept = {}, focus = null, a = document.activeElement;
            boxes().forEach(function (i) {
              kept[keyOf(i)] = i.value;
              if (i === a) focus = { k: keyOf(i), s: i.selectionStart, e: i.selectionEnd };
            });
            noteLogs();
            pageBusy = true;
            var fetchedAt = Date.now();
            get('/' + location.search, 15000).then(function (r) { return r.text(); }).then(function (t) {
              var d = new DOMParser().parseFromString(t, 'text/html'), sx = window.scrollX, sy = window.scrollY;
              // 500-C: each column of the wide layout scrolls on its own; keep where it was.
              var cols = {};
              document.querySelectorAll('#desk .col[id]').forEach(function (c) { cols[c.id] = c.scrollTop; });
              noteLogs();
              // `logs`, not `boxes`: the outer `boxes()` helper finds the form fields a refresh carries
              // across, and `var` in this function would hide it from the whole of refresh.
              var logs = liveBoxes();
              ['screen', 'desk'].forEach(function (id) {
                var n = d.getElementById(id), o = document.getElementById(id);
                if (n && o) o.replaceWith(document.importNode(n, true));
              });
              syncThemeStyles(d);
              keepBoxes(logs);
              keepStats();
              page = version(d);
              var mine = document.querySelector('meta[name=page-version]');
              if (mine) mine.setAttribute('content', page);
              viewsNow = viewsOf(d);
              boxes().forEach(function (i) {
                var k = keyOf(i);
                if (kept[k]) i.value = kept[k];
                if (noteBack[k]) { if (!i.value) i.value = noteBack[k]; delete noteBack[k]; }
                if (focus && focus.k === k) {
                  try { i.focus({ preventScroll: true }); } catch (x) { i.focus(); }
                  try { i.setSelectionRange(focus.s, focus.e); } catch (x) {}
                }
              });
              // The pool page's project dropdown keeps its choice: it lives in the browser (MP-P2).
              if (window.applyFilters) window.applyFilters();
              applyPlans();
              syncTheme();
              applyLogs();
              // 1004-C: the rebuilt desk draws Worker types closed; open it again if this browser keeps it open.
              applyWt();
              pinLogScroll();
              applyHl();
              // The desk was just rebuilt from the scheduler's view, which is behind the clicks the
              // developer has already made; put them back (957-B item 2). A landed one whose item this
              // page no longer shows is done with first (990-B).
              agreePending(fetchedAt);
              applyPending();
              window.scrollTo(sx, sy);
              Object.keys(cols).forEach(function (id) {
                var c = document.getElementById(id);
                if (c && cols[id]) c.scrollTop = cols[id];
              });
            }).catch(function () {}).then(function () {
              pageBusy = false;
              if (force) { submitting = false; document.body.style.cursor = ''; }
              if (pageAgain) { pageAgain = false; refresh(true); }
            });
          }
          // 1109-C: the page knows the build it was loaded from (meta build-version); /live.json and the
          // stream's frames carry the running build's version. When they differ a deploy happened and this
          // page's script is the old build's, so it reloads in full - but never under the guards of the
          // structure swap (a box being typed in, a dialog, selected text, a button's help), and then as
          // soon as those end. A note typed in a card's box is kept across the reload in sessionStorage,
          // keyed by project and card (1099-C), and put back by the next load.
          var buildMine = (function () { var m = document.querySelector('meta[name=build-version]'); return m ? m.getAttribute('content') : ''; })();
          var buildNew = '', buildWait = false;
          function saveNotes() {
            try { boxes().forEach(function (i) { if (i.name === 'note' && i.value) sessionStorage.setItem(keyOf(i), i.value); }); } catch (x) {}
          }
          function restoreNotes() {
            try {
              boxes().forEach(function (i) {
                if (i.name !== 'note') return;
                var k = keyOf(i), v = sessionStorage.getItem(k);
                if (v === null) return;
                sessionStorage.removeItem(k);
                if (!i.value) i.value = v;
              });
            } catch (x) {}
          }
          restoreNotes();
          function checkBuild(b) {
            if (typeof b === 'string' && b && buildMine && b !== buildMine) buildNew = b;
            if (!buildNew) return;
            if (submitting || userBusy()) {
              if (!buildWait) { buildWait = true; setTimeout(function () { buildWait = false; checkBuild(); }, 1000); }
              return;
            }
            saveNotes();
            location.reload();
          }
          // 1103-C: the live view arrives on one stream per open page. /live/stream (1102-C) pushes the
          // hub's frames (1101-B) - the log lines that grew, the stats line, the minutes and the
          // CPU/memory - at up to 24 a second and only when something changed, so the numbers move
          // smoothly instead of in two-second steps. While it is up the /live.json poll below still
          // runs, but for what a frame does not carry (a queued click, the health line, the views
          // version); when the stream errors or closes the page falls back to the poll's own painting
          // at once and asks for the stream again every AGAIN ms. Nothing is removed of the polling path.
          var AGAIN = 10000, CONTROL = 15000;
          var stream = null, streaming = false, streamAgain = 0, streamSeq = -1, frun = 0;
          function streamUrl() {
            var m = /[?&]project=([^&]*)/.exec(location.search);
            return '/live/stream' + (m ? '?project=' + m[1] : '');
          }
          // One frame: the slots that changed, the host's load, and the page version when it moved.
          // Frames only ever carry what moved since the one before, so a slot's lines are appended, and
          // one whose reset is set keeps nothing of the run before it (1093-C). An old or repeated
          // sequence number is dropped: the stream can hand the same frame twice after a reconnect.
          function applyFrame(j) {
            if (!j) return;
            if (typeof j.seq === 'number') {
              if (j.seq <= streamSeq) return;
              streamSeq = j.seq;
            }
            var slots = j.slots || {};
            Object.keys(slots).forEach(function (s) { applyFrameSlot(s, slots[s]); });
            if (j.load) setLoad(j.load);
            if (j.page && j.page !== page) refresh(false);
            checkBuild(j.build);
          }
          // A frame's slot, drawn by the same code that draws the poll's answer (490-C, 610-C, 1092-C):
          // the window, its stats line, its minutes and its time-limit bar. A frame names no run, so the
          // page numbers one itself: that is what makes a new run in the slot clear the old one's lines.
          function applyFrameSlot(s, v) {
            var h = slotHist(s), got = v.lines || [];
            if (v.reset) { h.run = ''; h.n = 0; h.lines = []; }
            if (!h.run) h.run = 'f' + (++frun);
            applySlot(s, { run: h.run, log: got, next: h.n + got.length, stats: v.stats || '', time: v.time || '' });
          }
          // The title bar's CPU and memory, in the same words the server drew (HostLoadText): a frame
          // carries no core count, so the one already on screen is kept (1092-C's keep-last-value rule).
          function setLoad(l) {
            var n = document.getElementById('hostload');
            if (!n) return;
            var cpu = typeof l.cpuSmooth === 'number' ? l.cpuSmooth : l.cpu;
            if (typeof cpu !== 'number') return;
            var cores = /, (\d+ threads)/.exec(n.textContent || '');
            setText(n, 'CPU ' + cpu + '%, ' + Math.floor(l.freeMemoryBytes / 1073741824) + 'G free of '
              + Math.floor(l.totalMemoryBytes / 1073741824) + 'G' + (cores ? ', ' + cores[1] : ''));
          }
          // Up: the frames paint the worker windows. Down: the poll paints them again, and the windows
          // the stream had filled start over, because the feed numbers its own lines and runs. The
          // stream is closed and re-armed after AGAIN ms even if it died before its first frame.
          function streamUp() { streaming = true; }
          function streamDown() {
            if (stream) { try { stream.close(); } catch (x) {} stream = null; }
            if (streaming) { streaming = false; hist = {}; held = {}; streamSeq = -1; }
            streamAgain = Date.now() + AGAIN;
            schedule();
          }
          function openStream() {
            if (stream || !window.EventSource) return;
            streamAgain = 0;
            try { stream = new EventSource(streamUrl()); } catch (x) { stream = null; return; }
            stream.onmessage = function (e) {
              streamUp();
              var j = null;
              try { j = JSON.parse(e.data); } catch (x) { return; }
              applyFrame(j);
            };
            stream.onerror = streamDown;
          }
          function schedule() {
            clearTimeout(timer);
            var ms = Date.now() < lively ? INTERACT : waitFor ? FAST : EVERY;
            // 1103-C: while the stream is up the frames carry the live view, so the poll is not what
            // moves the numbers; it keeps a slow cadence for what a frame does not carry (a queued
            // click's state, the health line, the views version). A click still polls fast.
            if (streaming && !waitFor) ms = CONTROL;
            timer = setTimeout(live, ms);
          }
          // 966-B: each click or key (re)starts fast mode; the first one asks at once.
          function interact() { var was = Date.now() < lively; lively = Date.now() + LIVELY; if (!was && !liveBusy) schedule(); }
          document.addEventListener('pointerdown', interact, true);
          document.addEventListener('keydown', interact, true);
          // The live view: one request at a time, none while the tab is hidden unless a click waits.
          function live() {
            if (liveBusy) return;
            if (document.hidden && !waitFor) { schedule(); return; }
            // 1103-C: the stream is the live view while it is up; it is (re)asked for every AGAIN ms
            // after it drops. The poll below still runs either way, for the control fields.
            if (!stream && Date.now() >= streamAgain) openStream();
            liveBusy = true;
            var mine = ++asked;
            get('/live.json' + sinceParam(), 4000).then(function (r) { return r.json(); }).then(function (j) {
              down(false);
              checkBuild(j.build);
              // The windows and their numbers come from the frames while the stream is up: the feed
              // numbers its own lines and runs, and the two would clear each other's window (610-C).
              if (!streaming) {
              var slots = j.slots || {};
              Object.keys(slots).forEach(function (s) { applySlot(s, slots[s]); });
              // 1093-C: a worker window with no entry in this answer is a slot between two cards - its
              // worker ended and the next one is not listed yet. It must not go on showing the card
              // before it under the new title, so it says what is starting instead.
              document.querySelectorAll('pre.log[data-slot]').forEach(function (p) {
                var s = p.getAttribute('data-slot');
                if (slots[s]) return;
                var card = windowOf(p);
                if (card && p.textContent !== startingText(card)) clearSlot(s, card);
              });
              }
              // How far each of this page's clicks has got on the queue (957-B item 3): the marks the
              // click made are replaced when its work really lands.
              settlePending(j.actions || []);
              // The Check main button follows the check the snapshot says is running (930-C). While a
              // click of its own is still waiting to show, its working state is kept.
              if (typeof j.checking === 'string' && (j.checking || !submitting)) {
                healthButton(j.checking ? 'Checking ' + j.checking + '...' : 'Check main', !!j.checking);
              }
              if (waitFor) {
                if (j.pageDone && mine >= waitFrom) { waitFor = 0; refresh(true); }
              } else if (j.page && j.page !== page) {
                refresh(false);
              } else if (document.querySelector('meta[name=views-version]') && typeof j.views === 'string' && j.views !== viewsNow) {
                refresh(false);
              }
            }).catch(function () { down(true); }).then(function () {
              liveBusy = false;
              if (waitFor && Date.now() - waitSince > 60000) { waitFor = 0; location.reload(); return; }
              schedule();
            });
          }
          // A button is sent in the background and reads 'Working...' until its result shows.
          document.addEventListener('submit', function (e) {
            var f = e.target;
            if (!f || String(f.method).toLowerCase() !== 'post' || !window.fetch || !window.URLSearchParams || !window.FormData) return;
            e.preventDefault();
            // 1033-C: a Workspaces box changed again while its last save is in flight posts too (the queue keeps the order).
            if (submitting && f.getAttribute('action') !== '/workspaces') return;
            submitting = true;
            var b = e.submitter || f.querySelector('button'), body = new URLSearchParams(new FormData(f));
            if (b && b.name) body.append(b.name, b.value);
            // 1099-C: a button outside its form names its own action (formaction); a query on it (the question) joins the body.
            var act = (b && b.getAttribute && b.getAttribute('formaction')) || f.getAttribute('action'), qs = act.indexOf('?');
            if (qs >= 0) { new URLSearchParams(act.slice(qs + 1)).forEach(function (v, k) { body.set(k, v); }); act = act.slice(0, qs); }
            var noteBoxes = f.querySelectorAll('input[name=note]'), noteText = noteBoxes.length ? noteBoxes[0].value : '';
            if (b) { b.textContent = 'Working...'; b.style.cursor = 'wait'; }
            // 930-C: a press of Check main is answered at once - pressed and disabled, before the
            // next poll brings the sha it is checking.
            if (f.getAttribute('action') === '/health-now') healthButton('Checking...', true);
            document.body.style.cursor = 'progress';
            var dlg = f.closest ? f.closest('dialog') : null;
            // The click's own result goes on the page before its work has even started (957-B item 2);
            // the queue's id tells the live poll which action to watch for.
            var mask = maskOf(act, body);
            if (mask) { pending.push(mask); applyPending(); }
            get(act, 15000, { method: 'POST', body: body, headers: { 'X-Requested-With': 'fetch' } })
              .then(function (r) { return r.json(); })
              .then(function (j) {
                if (dlg && dlg.close) dlg.close();
                f.querySelectorAll('input[name=text]').forEach(function (i) { i.value = ''; });
                // 1099-C: the note went with the post, so its box is empty again; a failed post keeps it (below).
                f.querySelectorAll('input[name=note]').forEach(function (i) { i.value = ''; });
                if (mask) { mask.id = j.id; }
                waitFor = j.id || 1; waitFrom = asked + 1; waitSince = Date.now();
                schedule();
              })
              .catch(function () {
                // 1099-C: a post that did not arrive keeps what was typed, and the page says so.
                if (noteText) {
                  submitting = false; document.body.style.cursor = '';
                  if (b && b.style) { b.style.cursor = ''; }
                  if (mask) { var at = pending.indexOf(mask); if (at >= 0) pending.splice(at, 1); }
                  sayNotice('The post failed, so your note was kept: press the button again.');
                  refresh(true);
                  return;
                }
                location.reload();
              });
          });
          // Depends-on links (card 510-B, as the old page): pointing at one lights up that card
          // wherever the page shows it (its rows, its worker's window); a click keeps it lit and
          // brings it into view; a click anywhere else clears it.
          var hlPinned = null, hlHover = null;
          function applyHl() {
            var want = hlHover || hlPinned;
            document.querySelectorAll('.hl').forEach(function (n) { if (n.getAttribute('data-card') !== want) n.classList.remove('hl'); });
            document.querySelectorAll('.dep').forEach(function (d) { d.classList.toggle('on', !!want && d.getAttribute('data-dep') === want); });
            if (want) document.querySelectorAll('[data-card="' + want + '"]').forEach(function (n) { n.classList.add('hl'); });
          }
          document.addEventListener('mouseover', function (e) {
            var d = e.target.closest ? e.target.closest('.dep') : null;
            if (d && hlHover !== d.getAttribute('data-dep')) { hlHover = d.getAttribute('data-dep'); applyHl(); }
          });
          document.addEventListener('mouseout', function (e) {
            var d = e.target.closest ? e.target.closest('.dep') : null;
            if (d && !(e.relatedTarget && d.contains(e.relatedTarget))) { hlHover = null; applyHl(); }
          });
          document.addEventListener('click', function (e) {
            var d = e.target.closest ? e.target.closest('.dep') : null;
            if (!d) { if (hlPinned) { hlPinned = null; applyHl(); } return; }
            var id = d.getAttribute('data-dep');
            hlPinned = (hlPinned === id) ? null : id;
            applyHl();
            if (!hlPinned) return;
            var own = d.closest('[data-card]'), all = document.querySelectorAll('[data-card="' + id + '"]');
            for (var i = 0; i < all.length; i++) {
              if (all[i] !== own) { all[i].scrollIntoView({ block: 'center', behavior: 'smooth' }); break; }
            }
          });
          // (The appearance selector's change is wired in the head, with the themes: PageThemes.)
          document.addEventListener('visibilitychange', function () { if (!document.hidden) live(); });
          // Back to a page the browser kept as it was (with a 'Working...' button): load it afresh.
          window.addEventListener('pageshow', function (e) { if (e.persisted) location.reload(); });
          applyPlans();
          syncTheme();
          applyLogs();
          applyWt();
          pinLogScroll();
          if (window.fetch) live();
        })();
        </script>
        """;
}

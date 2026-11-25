// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Tests.Views;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Server;

/// <summary>1099-C: a note typed on the page reaches its own card, whichever button is pressed, and only that card.</summary>
public sealed class CardNoteTests
{
    private static CardQuestion Question(int number) => new()
    {
        Number = number,
        Text = "Which?",
        Choices = [new CardChoice { Label = "yes" }, new CardChoice { Label = "no" }],
    };

    private static PageSnapshot TwoCards() => ViewsFixture.Full() with
    {
        Waiting =
        [
            new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "1078-B-first.md", Note = "q", Questions = [Question(1)] },
            new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "1079-B-second.md", Note = "q", Questions = [Question(1)] },
            new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "1080-B-third.md", Note = "look" },
            new PageWaitingItem { Kind = PageWaitingKind.Stuck, Name = "1081-B-fourth.md", Note = "stuck" },
        ],
    };

    // Each card has its own note form (its own id, carrying its card), and every one of its buttons
    // belongs to that form, so a button can only ever send the note of its own card.
    [Fact]
    public void EachCard_HasItsOwnNoteForm_AndItsButtonsBelongToIt()
    {
        var html = new PageRenderer().Render(TwoCards(), remote: false, controlsAllowed: true);

        foreach (var (id, card) in new[] { ("n-1078-B", "1078-B-first.md"), ("n-1079-B", "1079-B-second.md"), ("n-1080-B", "1080-B-third.md"), ("n-1081-B", "1081-B-fourth.md") })
        {
            Assert.Contains($"<form id='{id}' method='post' action='/note' class='g'", html, StringComparison.Ordinal);
            Assert.Contains($"<input type='hidden' name='card' value='{card}'>", html, StringComparison.Ordinal);
        }

        Assert.Equal(4, Regex.Matches(html, "<input name='note'").Count);
        Assert.Contains("<button form='n-1078-B' formaction='/answer?q=1' name='a' value='0'", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-1079-B' formaction='/answer?q=1' name='a' value='1'", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-1080-B' formaction='/done'", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-1081-B' formaction='/retry'", html, StringComparison.Ordinal);
        Assert.Contains(">Save note</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("form='n-1078-B' formaction='/done'", html, StringComparison.Ordinal);
    }

    // The pool page keys the form by project as well, so one project's card cannot take another's note.
    [Fact]
    public void Pool_NoteForm_IsKeyedByProjectAndCard()
    {
        var a = TwoCards() with { Project = "AmigaX" };
        var b = TwoCards() with { Project = "Other" };
        var html = new CombinedPageRenderer().Render([a, b], "", remote: false, controlsAllowed: true);

        Assert.Contains("<form id='n-AmigaX-1078-B' method='post' action='/note' class='g'", html, StringComparison.Ordinal);
        Assert.Contains("<form id='n-Other-1078-B' method='post' action='/note' class='g'", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-Other-1080-B' formaction='/done'", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-AmigaX-1081-B' formaction='/retry'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlsNotAllowed_NoNoteForm()
    {
        var html = new PageRenderer().Render(TwoCards(), remote: true, controlsAllowed: false);

        Assert.DoesNotContain("action='/note'", html, StringComparison.Ordinal);
    }

    // The page script keys a note box by its project and card (not its place among the boxes, which was the
    // same for every card, so a refresh put one card's text into all of them), clears it only once the post
    // went through, and puts the text back when the post failed.
    [Fact]
    public void Script_KeysNoteBoxesByCard_ClearsOnSuccess_KeepsOnFailure()
    {
        var script = PageScript.Html;

        Assert.Contains("if (i.name === 'note') return noteKey(formField(i.form, 'project'), formField(i.form, 'card'));", script, StringComparison.Ordinal);
        Assert.Contains("f.querySelectorAll('input[name=note]').forEach(function (i) { i.value = ''; });", script, StringComparison.Ordinal);
        Assert.Contains("noteBack[noteKey(p.project, p.card)] = p.note;", script, StringComparison.Ordinal);
        Assert.Contains("getAttribute('formaction')", script, StringComparison.Ordinal);
        Assert.Contains("'/note': 'note'", script, StringComparison.Ordinal);
    }

    // Done, Retry, Send back and Save note each hand the typed note to the action for their own card.
    [Theory]
    [InlineData("/done", "1080-B-third.md", "/done")]
    [InlineData("/retry", "1081-B-fourth.md", "/retry")]
    [InlineData("/resume-card", "1080-B-third.md", "/resume-card")]
    [InlineData("/note", "1079-B-second.md", "/note")]
    public async Task EveryCardRoute_PassesTheNoteOfItsOwnCard(string route, string card, string kind)
    {
        using var web = new WebHarness();

        await web.PostFormAsync(route, $"card={card}&note=the+machine+is+busy");

        Assert.Equal(1, web.Actions.CallCount(kind));
        Assert.Equal(card, web.Actions.LastCard);
        Assert.Equal("the machine is busy", web.Actions.LastNote);
    }

    [Fact]
    public async Task Answer_FromAFormaction_CarriesTheQuestionInTheQuery()
    {
        using var web = new WebHarness();

        await web.PostFormAsync("/answer?q=1", "card=1079-B-second.md&a=0&note=for+this+one");

        Assert.Equal(1, web.Actions.CallCount("/answer"));
        Assert.Equal("1079-B-second.md|1|0|for this one", web.Actions.LastAnswer);
    }
}

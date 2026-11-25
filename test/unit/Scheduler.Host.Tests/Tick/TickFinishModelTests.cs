// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// ACCT-2 (request #022, known gap 285-B (b)): <c>TickHost.Finish</c> writes the accounting row's model
/// column with the same resolve the page uses, so it names the models.json key the run's model is rather
/// than the worker type's raw model string.
/// </summary>
[ParallelMethods]
public class TickFinishModelTests
{
    // The type's model is "opus"; models.json spells the same model "Opus", which is what the row must say.
    private const string Model = "opus";
    private const string Key = "Opus";
    private const string Workers = """
        {"pool":{"maxWorkers":1,"types":{
           "default":{"max":1,"agent":"","model":"opus","premium":false,"areas":""}}}}
        """;

    private const string Ends =
        "echo ARGS: %*\r\n"
        + "mkdir jobs\\done 2>nul\r\n"
        + "git mv jobs/active/010-B-x.md jobs/done/010-B-x.md\r\n"
        + "git -c user.name=w -c user.email=w@w commit -qm \"jobs: done 010-B-x\"\r\n"
        + "git push -q origin HEAD:main\r\n"
        + "echo LANE-RESULT: done\r\n";

    // The row names the models.json key, as the page's own resolve does.
    [Fact]
    public void Finish_ModelsJson_TheRowNamesTheKey()
    {
        using var rig = Rig("""{"Opus":{"short":"opus"}}""");
        Assert.True(rig.TickUntilSettled(() => Row(rig) is not null), string.Join("\n", rig.Log.Lines));
        Assert.Equal(Key, Row(rig)!);
    }

    // With no models.json there is no key to name, so the row keeps the worker's model as today.
    [Fact]
    public void Finish_NoModelsJson_TheRowNamesTheModel()
    {
        using var rig = Rig(null);
        Assert.True(rig.TickUntilSettled(() => Row(rig) is not null), string.Join("\n", rig.Log.Lines));
        Assert.Equal(Model, Row(rig)!);
    }

    private static TickRig Rig(string? modelsJson)
    {
        var cards = new Dictionary<string, string>
        {
            [$"jobs/queue/{TickRig.CardName}.md"] = TickRig.CardText,
        };
        if (modelsJson is not null)
        {
            cards["jobs/models.json"] = modelsJson;
        }

        return new TickRig(Ends, workersJson: Workers, cards: cards);
    }

    // The model column (index 8) of the finished run's row, or null until the row is written.
    private static string? Row(TickRig rig)
    {
        var csv = Path.Combine(rig.LogDir, "accounting.csv");
        if (!File.Exists(csv))
        {
            return null;
        }

        var lines = File.ReadAllLines(csv);
        for (var i = lines.Length - 1; i > 0; i--)
        {
            var fields = lines[i].Split(',');
            if (fields.Length > 8 && fields[1] == TickRig.CardName)
            {
                return fields[8];
            }
        }

        return null;
    }
}

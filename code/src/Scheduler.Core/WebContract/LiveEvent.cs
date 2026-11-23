namespace Scheduler.Core.WebContract;

/// <summary>A rendered worker-output event (PAGE-10): a mark and its text.</summary>
public sealed record LiveEvent(LiveMark Mark, string Text)
{
    /// <summary>The character the rendered line starts with, or empty for marks without one.</summary>
    public string Prefix => Mark switch
    {
        LiveMark.Reasoning => "~",
        LiveMark.Message => "*",
        LiveMark.ToolCall => ">",
        LiveMark.FailedTool => "x",
        LiveMark.Error => "!",
        _ => "",
    };
}

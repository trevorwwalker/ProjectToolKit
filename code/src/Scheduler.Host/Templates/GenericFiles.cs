namespace Scheduler.Host.Templates;

/// <summary>One generic file: its template under the toolkit's <c>assets/templates/</c> and its copy in the project.</summary>
/// <param name="Template">The template's path under <c>assets/templates/</c>, as <c>jobs/README.md</c>.</param>
/// <param name="Target">The copy's path in the project, as <c>jobs/README.md</c>.</param>
public sealed record GenericFile(string Template, string Target);

/// <summary>
/// The generic files (roadmap item 1, card 540-B): copies of ProjectToolkit's templates that a project
/// never edits. The list is exactly what <c>bin/projecttoolkit.ps1</c> writes with the generic flag
/// (<c>Put ... $true</c>, refreshed by <c>-Update</c>); a new generic file is added to both.
/// Project-specific files (<c>project.md</c>, <c>categories.json</c>, <c>workers.json</c>,
/// <c>scheduler.json</c>, <c>models.json</c>) are never among them.
/// </summary>
public static class GenericFiles
{
    /// <summary>The folder under the toolkit that holds the templates.</summary>
    public const string TemplatesDir = "assets/templates";

    /// <summary>The generic files of a project whose queue is <paramref name="jobsDir"/> (<c>jobs</c>, or <c>docs/jobs</c> for an older project).</summary>
    public static IReadOnlyList<GenericFile> For(string jobsDir)
    {
        var q = jobsDir.Replace('\\', '/').Trim('/');
        return
        [
            new("gitattributes", ".gitattributes"),
            new("jobs/README.md", $"{q}/README.md"),
            new("jobs/card-template.md", $"{q}/card-template.md"),
            new("jobs/plan-template.md", $"{q}/plan-template.md"),
            new("jobs/card-template-feature.md", $"{q}/card-template-feature.md"),
            new("jobs/card-template-bug.md", $"{q}/card-template-bug.md"),
            new("jobs/attachments/README.md", $"{q}/attachments/README.md"),
            new("jobs/requests/README.md", $"{q}/requests/README.md"),
            new("ai/README.md", "docs/ai/README.md"),
        ];
    }
}

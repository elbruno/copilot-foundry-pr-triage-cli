namespace RepoTriage.Cli.Models;

/// <summary>
/// The final output of the triage workflow.
/// </summary>
public sealed record TriageResult(
    string Summary,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Checklist,
    string SuggestedPrCommentMarkdown
);

namespace RepoTriage.Cli.Models;

/// <summary>
/// Describes a pull request or local diff to triage.
/// </summary>
public sealed record PullRequestInput(
    string Title,
    string Body,
    string Diff,
    IReadOnlyList<string> FilesChanged
);

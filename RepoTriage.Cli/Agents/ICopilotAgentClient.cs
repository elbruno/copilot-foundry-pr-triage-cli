using Microsoft.Agents.AI;
using RepoTriage.Cli.Models;

namespace RepoTriage.Cli.Agents;

/// <summary>
/// Abstraction for the Copilot Agent that handles GitHub/PR context operations
/// using the Microsoft Agent Framework.
/// </summary>
public interface ICopilotAgentClient : IAsyncDisposable
{
    /// <summary>Initializes the agent client (connects to Copilot CLI session).</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Fetches pull request metadata from a GitHub PR URL.</summary>
    Task<PullRequestInput> GetPullRequestAsync(Uri prUrl, CancellationToken ct);

    /// <summary>Fetches the diff for a pull request.</summary>
    Task<string> GetDiffAsync(PullRequestInput pr, CancellationToken ct);

    /// <summary>Drafts a PR review comment in Markdown.</summary>
    Task<string> DraftCommentAsync(string summary, IReadOnlyList<string> risks, IReadOnlyList<string> checklist, string prTitle, CancellationToken ct);

    /// <summary>Drafts a PR review comment in Markdown with streaming support.</summary>
    IAsyncEnumerable<string> DraftCommentStreamingAsync(string summary, IReadOnlyList<string> risks, IReadOnlyList<string> checklist, string prTitle, CancellationToken ct);
}

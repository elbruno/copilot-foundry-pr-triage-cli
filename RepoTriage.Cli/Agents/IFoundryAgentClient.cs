using Microsoft.Agents.AI;

namespace RepoTriage.Cli.Agents;

/// <summary>
/// Abstraction for the Foundry Local Agent (local LLM) used for
/// summarization, risk detection, and checklist generation using
/// Microsoft Agent Framework with ChatClientAgent.
/// See https://www.foundrylocal.ai/ for more information.
/// </summary>
public interface IFoundryAgentClient : IAsyncDisposable
{
    /// <summary>Initializes the agent client (creates IChatClient and agents).</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Sends a prompt to the local LLM and returns the completion.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);

    /// <summary>Sends a prompt to the local LLM and returns the completion with streaming support.</summary>
    IAsyncEnumerable<string> CompleteStreamingAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

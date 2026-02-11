namespace RepoTriage.Cli.Agents;

/// <summary>
/// Abstraction for the Foundry Local Agent (local LLM) used for
/// summarization, risk detection, and checklist generation.
/// See https://www.foundrylocal.ai/ for more information.
/// </summary>
public interface IFoundryAgentClient
{
    /// <summary>Sends a prompt to the local LLM and returns the completion.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

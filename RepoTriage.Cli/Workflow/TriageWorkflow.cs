using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoTriage.Cli.Agents;
using RepoTriage.Cli.Models;
using RepoTriage.Cli.Ui;

namespace RepoTriage.Cli.Workflow;

/// <summary>
/// Orchestrates the five-step triage workflow, delegating work to the
/// Copilot Agent and Foundry Local Agent while reporting live progress.
/// 
/// EDUCATIONAL NOTE - Multi-Agent Orchestration:
/// This workflow demonstrates key Agent Framework patterns:
///   1. Separation of Concerns: GitHub ops (Copilot) vs AI analysis (Foundry)
///   2. Sequential Pipeline: Each step feeds into the next
///   3. Error Handling: Retry logic for network resilience
///   4. Streaming: Real-time token display for better UX
/// 
/// Designed to be simple and easy to follow for 5-10 minute live demos.
/// Each step is clearly labeled and uses descriptive variable names.
/// </summary>
public sealed class TriageWorkflow
{
    private readonly ICopilotAgentClient _copilotAgent;
    private readonly IFoundryAgentClient _foundryAgent;
    private readonly ILogger<TriageWorkflow> _logger;

    public TriageWorkflow(
        ICopilotAgentClient copilotAgent,
        IFoundryAgentClient foundryAgent,
        ILogger<TriageWorkflow>? logger = null)
    {
        _copilotAgent = copilotAgent;
        _foundryAgent = foundryAgent;
        _logger = logger ?? NullLogger<TriageWorkflow>.Instance;
    }

    /// <summary>
    /// Runs the full triage pipeline and returns the result.
    /// Simple, non-streaming version for easy demo comprehension.
    /// 
    /// EDUCATIONAL NOTE - Why AIAgent.RunAsync() vs raw HTTP calls:
    ///   ✅ Agent: Structured prompts, session management, built-in retry
    ///   ❌ HTTP: Manual JSON, no context, verbose error handling
    /// The Agent Framework handles complexity so your code stays clean.
    /// </summary>
    public async Task<TriageResult> RunAsync(
        PullRequestInput input,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        try
        {
            // Step 1 — Fetch / validate context (Copilot Agent)
            progress.Report(StepProgress.Working(TriageStep.FetchContext));
            var diff = await RetryAsync(() => _copilotAgent.GetDiffAsync(input, ct), "Fetch PR context", ct: ct);
            progress.Report(StepProgress.Done(TriageStep.FetchContext));

            var prContext = $"Title: {input.Title}\n\n{input.Body}\n\nFiles changed:\n{string.Join("\n", input.FilesChanged)}";
            var diffContext = $"{prContext}\n\n--- Diff ---\n{diff}";

            // Step 2 — Summarize (Foundry Local Agent)
            progress.Report(StepProgress.Working(TriageStep.Summarize));
            var summary = await RetryAsync(
                () => _foundryAgent.CompleteAsync(Prompts.SummarizeSystem, Prompts.SummarizeUser(diffContext), ct),
                "Summarize changes",
                ct: ct);
            progress.Report(StepProgress.Done(TriageStep.Summarize));

            // Step 3 — Identify risks (Foundry Local Agent)
            progress.Report(StepProgress.Working(TriageStep.IdentifyRisks));
            var risksRaw = await RetryAsync(
                () => _foundryAgent.CompleteAsync(Prompts.RisksSystem, Prompts.RisksUser(diffContext), ct),
                "Identify risks",
                ct: ct);
            progress.Report(StepProgress.Done(TriageStep.IdentifyRisks));

            // Step 4 — Generate checklist (Foundry Local Agent)
            progress.Report(StepProgress.Working(TriageStep.GenerateChecklist));
            var checklistRaw = await RetryAsync(
                () => _foundryAgent.CompleteAsync(Prompts.ChecklistSystem, Prompts.ChecklistUser(diffContext), ct),
                "Generate checklist",
                ct: ct);
            progress.Report(StepProgress.Done(TriageStep.GenerateChecklist));

            // Step 5 — Draft PR comment (Copilot Agent)
            var risks = ParseBullets(risksRaw);
            var checklist = ParseBullets(checklistRaw);

            progress.Report(StepProgress.Working(TriageStep.DraftComment));
            var comment = await RetryAsync(
                () => _copilotAgent.DraftCommentAsync(summary, risks, checklist, input.Title, ct),
                "Draft PR comment",
                ct: ct);
            progress.Report(StepProgress.Done(TriageStep.DraftComment));

            return new TriageResult(summary.Trim(), risks, checklist, comment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed");
            throw;
        }
    }

    /// <summary>
    /// Runs the workflow with streaming token display for LLM responses.
    /// Shows real-time progress for a better user experience during demos.
    /// 
    /// EDUCATIONAL NOTE - Streaming Benefits:
    /// Streaming (RunStreamingAsync) provides immediate feedback:
    ///   - Tokens appear as LLM generates them (better perceived performance)
    ///   - Users see the "AI thinking" process
    ///   - Essential for live demos to show real-time generation
    ///   - Can cancel long-running operations mid-stream
    /// Perfect for 5-10 minute live presentations!
    /// </summary>
    public async Task<TriageResult> RunStreamingAsync(
        PullRequestInput input,
        StreamingProgress progress,
        CancellationToken ct)
    {
        try
        {
            // Step 1 — Fetch context
            progress.ReportStep(TriageStep.FetchContext, false);
            var diff = await RetryAsync(() => _copilotAgent.GetDiffAsync(input, ct), "Fetch PR context", ct: ct);
            progress.ReportStep(TriageStep.FetchContext, true);
            progress.ReportNewLine();

            var prContext = $"Title: {input.Title}\n\n{input.Body}\n\nFiles changed:\n{string.Join("\n", input.FilesChanged)}";
            var diffContext = $"{prContext}\n\n--- Diff ---\n{diff}";

            // Step 2 — Summarize with streaming
            progress.ReportStep(TriageStep.Summarize, false);
            progress.ReportNewLine();
            var summary = await StreamLLMResponseAsync(
                _foundryAgent.CompleteStreamingAsync(Prompts.SummarizeSystem, Prompts.SummarizeUser(diffContext), ct),
                progress,
                ct);
            progress.ReportNewLine();
            progress.ReportStep(TriageStep.Summarize, true);
            progress.ReportNewLine();

            // Step 3 — Identify risks with streaming
            progress.ReportStep(TriageStep.IdentifyRisks, false);
            progress.ReportNewLine();
            var risksRaw = await StreamLLMResponseAsync(
                _foundryAgent.CompleteStreamingAsync(Prompts.RisksSystem, Prompts.RisksUser(diffContext), ct),
                progress,
                ct);
            progress.ReportNewLine();
            progress.ReportStep(TriageStep.IdentifyRisks, true);
            progress.ReportNewLine();

            // Step 4 — Generate checklist with streaming
            progress.ReportStep(TriageStep.GenerateChecklist, false);
            progress.ReportNewLine();
            var checklistRaw = await StreamLLMResponseAsync(
                _foundryAgent.CompleteStreamingAsync(Prompts.ChecklistSystem, Prompts.ChecklistUser(diffContext), ct),
                progress,
                ct);
            progress.ReportNewLine();
            progress.ReportStep(TriageStep.GenerateChecklist, true);
            progress.ReportNewLine();

            // Step 5 — Draft comment
            var risks = ParseBullets(risksRaw);
            var checklist = ParseBullets(checklistRaw);

            progress.ReportStep(TriageStep.DraftComment, false);
            var comment = await RetryAsync(
                () => _copilotAgent.DraftCommentAsync(summary, risks, checklist, input.Title, ct),
                "Draft PR comment",
                ct: ct);
            progress.ReportStep(TriageStep.DraftComment, true);
            progress.ReportNewLine();

            return new TriageResult(summary.Trim(), risks, checklist, comment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming workflow execution failed");
            throw;
        }
    }

    /// <summary>
    /// Simple retry logic with exponential backoff for LLM calls.
    /// Makes demos more reliable when network is flaky.
    /// 
    /// EDUCATIONAL NOTE - Why Retry Logic:
    /// LLM calls can fail due to:
    ///   - Network timeouts
    ///   - Rate limiting
    ///   - Service restarts
    /// Exponential backoff (2s, 4s, 8s) gives the service time to recover
    /// without hammering it with requests. Essential for production use.
    /// </summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> operation, string operationName, int maxRetries = 3, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff: 2s, 4s, 8s
                _logger.LogWarning(ex, "{Operation} failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}s...",
                    operationName, attempt, maxRetries, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }

        // Last attempt without catching
        return await operation();
    }

    /// <summary>
    /// Consumes streaming tokens and displays them as they arrive.
    /// Accumulates tokens into final result string.
    /// 
    /// EDUCATIONAL NOTE - Streaming Pattern:
    /// IAsyncEnumerable&lt;string&gt; is the .NET pattern for async streaming:
    ///   - await foreach consumes items as they're produced
    ///   - WithCancellation() enables cancellation mid-stream
    ///   - StringBuilder accumulates for final result
    /// This pattern works for any streaming data source, not just LLMs.
    /// </summary>
    private static async Task<string> StreamLLMResponseAsync(
        IAsyncEnumerable<string> stream,
        StreamingProgress progress,
        CancellationToken ct = default)
    {
        var builder = new StringBuilder();
        await foreach (var token in stream.WithCancellation(ct))
        {
            builder.Append(token);
            progress.ReportToken(token);
        }
        return builder.ToString();
    }

    private static List<string> ParseBullets(string raw)
    {
        return raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '*', ' ', '\t'))
            .Where(line => line.Length > 0)
            .ToList();
    }
}

/// <summary>Progress payload for each workflow step.</summary>
public sealed record StepProgress(TriageStep Step, bool IsComplete)
{
    public static StepProgress Working(TriageStep step) => new(step, false);
    public static StepProgress Done(TriageStep step) => new(step, true);
}

/// <summary>Prompt templates kept short and deterministic.</summary>
internal static class Prompts
{
    public const string SummarizeSystem =
        "You are a code-review assistant. Summarize the following code change concisely in 3-5 bullet points. Be specific about what changed.";

    public static string SummarizeUser(string diffContext) =>
        $"Summarize this change:\n\n{diffContext}";

    public const string RisksSystem =
        "You are a senior code reviewer. Identify potential risks in this change: tests, breaking changes, performance, security, or documentation gaps. Output max 5 bullet points.";

    public static string RisksUser(string diffContext) =>
        $"Identify risks in this change:\n\n{diffContext}";

    public const string ChecklistSystem =
        "You are a code-review assistant. Generate a concise review checklist (max 8 items) for a reviewer to verify. Output as bullet points.";

    public static string ChecklistUser(string diffContext) =>
        $"Generate a review checklist for this change:\n\n{diffContext}";
}

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
/// Designed to be simple and easy to follow for 5-10 minute live demos.
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
            var diff = await RetryAsync(() => _copilotAgent.GetDiffAsync(input, ct), "Fetch PR context");
            progress.Report(StepProgress.Done(TriageStep.FetchContext));

            var prContext = $"Title: {input.Title}\n\n{input.Body}\n\nFiles changed:\n{string.Join("\n", input.FilesChanged)}";
            var diffContext = $"{prContext}\n\n--- Diff ---\n{diff}";

            // Step 2 — Summarize (Foundry Local Agent)
            progress.Report(StepProgress.Working(TriageStep.Summarize));
            var summary = await RetryAsync(
                () => _foundryAgent.CompleteAsync(Prompts.SummarizeSystem, Prompts.SummarizeUser(diffContext), ct),
                "Summarize changes");
            progress.Report(StepProgress.Done(TriageStep.Summarize));

            // Step 3 — Identify risks (Foundry Local Agent)
            progress.Report(StepProgress.Working(TriageStep.IdentifyRisks));
            var risksRaw = await RetryAsync(
                () => _foundryAgent.CompleteAsync(Prompts.RisksSystem, Prompts.RisksUser(diffContext), ct),
                "Identify risks");
            progress.Report(StepProgress.Done(TriageStep.IdentifyRisks));

            // Step 4 — Generate checklist (Foundry Local Agent)
            progress.Report(StepProgress.Working(TriageStep.GenerateChecklist));
            var checklistRaw = await RetryAsync(
                () => _foundryAgent.CompleteAsync(Prompts.ChecklistSystem, Prompts.ChecklistUser(diffContext), ct),
                "Generate checklist");
            progress.Report(StepProgress.Done(TriageStep.GenerateChecklist));

            // Step 5 — Draft PR comment (Copilot Agent)
            var risks = ParseBullets(risksRaw);
            var checklist = ParseBullets(checklistRaw);

            progress.Report(StepProgress.Working(TriageStep.DraftComment));
            var comment = await RetryAsync(
                () => _copilotAgent.DraftCommentAsync(summary, risks, checklist, input.Title, ct),
                "Draft PR comment");
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
            var diff = await RetryAsync(() => _copilotAgent.GetDiffAsync(input, ct), "Fetch PR context");
            progress.ReportStep(TriageStep.FetchContext, true);
            progress.ReportNewLine();

            var prContext = $"Title: {input.Title}\n\n{input.Body}\n\nFiles changed:\n{string.Join("\n", input.FilesChanged)}";
            var diffContext = $"{prContext}\n\n--- Diff ---\n{diff}";

            // Step 2 — Summarize with streaming
            progress.ReportStep(TriageStep.Summarize, false);
            progress.ReportNewLine();
            var summary = await StreamLLMResponseAsync(
                _foundryAgent.CompleteStreamingAsync(Prompts.SummarizeSystem, Prompts.SummarizeUser(diffContext), ct),
                progress);
            progress.ReportNewLine();
            progress.ReportStep(TriageStep.Summarize, true);
            progress.ReportNewLine();

            // Step 3 — Identify risks with streaming
            progress.ReportStep(TriageStep.IdentifyRisks, false);
            progress.ReportNewLine();
            var risksRaw = await StreamLLMResponseAsync(
                _foundryAgent.CompleteStreamingAsync(Prompts.RisksSystem, Prompts.RisksUser(diffContext), ct),
                progress);
            progress.ReportNewLine();
            progress.ReportStep(TriageStep.IdentifyRisks, true);
            progress.ReportNewLine();

            // Step 4 — Generate checklist with streaming
            progress.ReportStep(TriageStep.GenerateChecklist, false);
            progress.ReportNewLine();
            var checklistRaw = await StreamLLMResponseAsync(
                _foundryAgent.CompleteStreamingAsync(Prompts.ChecklistSystem, Prompts.ChecklistUser(diffContext), ct),
                progress);
            progress.ReportNewLine();
            progress.ReportStep(TriageStep.GenerateChecklist, true);
            progress.ReportNewLine();

            // Step 5 — Draft comment
            var risks = ParseBullets(risksRaw);
            var checklist = ParseBullets(checklistRaw);

            progress.ReportStep(TriageStep.DraftComment, false);
            var comment = await RetryAsync(
                () => _copilotAgent.DraftCommentAsync(summary, risks, checklist, input.Title, ct),
                "Draft PR comment");
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
    /// </summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> operation, string operationName, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff: 2s, 4s, 8s
                _logger.LogWarning(ex, "{Operation} failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}s...",
                    operationName, attempt, maxRetries, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        // Last attempt without catching
        return await operation();
    }

    /// <summary>
    /// Consumes streaming tokens and displays them as they arrive.
    /// Accumulates tokens into final result string.
    /// </summary>
    private static async Task<string> StreamLLMResponseAsync(
        IAsyncEnumerable<string> stream,
        StreamingProgress progress)
    {
        var builder = new StringBuilder();
        await foreach (var token in stream)
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

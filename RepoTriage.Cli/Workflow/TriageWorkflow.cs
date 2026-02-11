using RepoTriage.Cli.Agents;
using RepoTriage.Cli.Models;
using RepoTriage.Cli.Ui;

namespace RepoTriage.Cli.Workflow;

/// <summary>
/// Orchestrates the five-step triage workflow, delegating work to the
/// Copilot Agent and Foundry Local Agent while reporting live progress.
/// </summary>
public sealed class TriageWorkflow(
    ICopilotAgentClient copilotAgent,
    IFoundryAgentClient foundryAgent)
{
    /// <summary>
    /// Runs the full triage pipeline and returns the result.
    /// </summary>
    public async Task<TriageResult> RunAsync(
        PullRequestInput input,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        // Step 1 — Fetch / validate context (Copilot Agent)
        progress.Report(StepProgress.Working(TriageStep.FetchContext));
        var diff = await copilotAgent.GetDiffAsync(input, ct);
        progress.Report(StepProgress.Done(TriageStep.FetchContext));

        var prContext = $"Title: {input.Title}\n\n{input.Body}\n\nFiles changed:\n{string.Join("\n", input.FilesChanged)}";
        var diffContext = $"{prContext}\n\n--- Diff ---\n{diff}";

        // Step 2 — Summarize (Foundry Local Agent)
        progress.Report(StepProgress.Working(TriageStep.Summarize));
        var summary = await foundryAgent.CompleteAsync(
            Prompts.SummarizeSystem, Prompts.SummarizeUser(diffContext), ct);
        progress.Report(StepProgress.Done(TriageStep.Summarize));

        // Step 3 — Identify risks (Foundry Local Agent)
        progress.Report(StepProgress.Working(TriageStep.IdentifyRisks));
        var risksRaw = await foundryAgent.CompleteAsync(
            Prompts.RisksSystem, Prompts.RisksUser(diffContext), ct);
        progress.Report(StepProgress.Done(TriageStep.IdentifyRisks));

        // Step 4 — Generate checklist (Foundry Local Agent)
        progress.Report(StepProgress.Working(TriageStep.GenerateChecklist));
        var checklistRaw = await foundryAgent.CompleteAsync(
            Prompts.ChecklistSystem, Prompts.ChecklistUser(diffContext), ct);
        progress.Report(StepProgress.Done(TriageStep.GenerateChecklist));

        // Step 5 — Draft PR comment (Copilot Agent)
        var risks = ParseBullets(risksRaw);
        var checklist = ParseBullets(checklistRaw);

        progress.Report(StepProgress.Working(TriageStep.DraftComment));
        var comment = await copilotAgent.DraftCommentAsync(
            summary, risks, checklist, input.Title, ct);
        progress.Report(StepProgress.Done(TriageStep.DraftComment));

        return new TriageResult(summary.Trim(), risks, checklist, comment);
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

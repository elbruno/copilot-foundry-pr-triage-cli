namespace RepoTriage.Cli.Workflow;

/// <summary>
/// Defines each step in the triage workflow with its agent label and description.
/// </summary>
public sealed record TriageStep(string AgentLabel, string Name, string Emoji)
{
    public static readonly TriageStep FetchContext     = new("Copilot Agent",        "Fetch PR context / Load diff",    "🤖");
    public static readonly TriageStep Summarize        = new("Foundry Local Agent",  "Summarize the change set",        "🧠");
    public static readonly TriageStep IdentifyRisks    = new("Foundry Local Agent",  "Identify risks",                  "🧠");
    public static readonly TriageStep GenerateChecklist = new("Foundry Local Agent", "Generate review checklist",       "🧠");
    public static readonly TriageStep DraftComment     = new("Copilot Agent",        "Draft PR comment",                "🤖");

    public static IReadOnlyList<TriageStep> All =>
    [
        FetchContext,
        Summarize,
        IdentifyRisks,
        GenerateChecklist,
        DraftComment
    ];
}

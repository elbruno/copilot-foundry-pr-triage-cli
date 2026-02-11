using RepoTriage.Cli.Models;
using RepoTriage.Cli.Workflow;
using Spectre.Console;

namespace RepoTriage.Cli.Ui;

/// <summary>
/// Spectre.Console helpers for rendering the triage agent UI.
/// </summary>
public static class ConsoleUi
{
    /// <summary>Renders the application header.</summary>
    public static void RenderHeader()
    {
        AnsiConsole.Write(new Rule("[bold blue]🔍 Repo Triage Agent[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine("[dim]Powered by Copilot Agent + Foundry Local Agent[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Runs the triage workflow inside a live Spectre.Console status display,
    /// showing which agent is working at each step with streaming token display.
    /// </summary>
    public static async Task<TriageResult> RunWithProgressAsync(
        TriageWorkflow workflow, PullRequestInput input, CancellationToken ct)
    {
        // Track step states for the live table
        var stepStates = new Dictionary<TriageStep, string>();
        foreach (var step in TriageStep.All)
        {
            stepStates[step] = "[dim]Pending[/]";
        }

        TriageResult? result = null;

        await AnsiConsole.Live(BuildTable(stepStates))
            .StartAsync(async ctx =>
            {
                var progress = new Progress<StepProgress>(p =>
                {
                    stepStates[p.Step] = p.IsComplete
                        ? "[green]✅ Done[/]"
                        : "[yellow]⏳ Working…[/]";
                    ctx.UpdateTarget(BuildTable(stepStates));
                });

                result = await workflow.RunAsync(input, progress, ct);
                ctx.UpdateTarget(BuildTable(stepStates));
            });

        return result!;
    }

    /// <summary>
    /// Runs the triage workflow with streaming token display for LLM responses.
    /// Shows tokens as they arrive for a better UX during live demos.
    /// </summary>
    public static async Task<TriageResult> RunWithStreamingAsync(
        TriageWorkflow workflow, PullRequestInput input, CancellationToken ct)
    {
        TriageResult? result = null;

        await AnsiConsole.Status()
            .StartAsync("[yellow]Running triage workflow...[/]", async ctx =>
            {
                var streamingProgress = new StreamingProgress(ctx);
                result = await workflow.RunStreamingAsync(input, streamingProgress, ct);
            });

        return result!;
    }

    private static Table BuildTable(Dictionary<TriageStep, string> states)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Agent[/]")
            .AddColumn("[bold]Step[/]")
            .AddColumn("[bold]Status[/]");

        foreach (var step in TriageStep.All)
        {
            var status = states.TryGetValue(step, out var s) ? s : "[dim]Pending[/]";
            table.AddRow(
                $"{step.Emoji} [bold]{Markup.Escape(step.AgentLabel)}[/]",
                Markup.Escape(step.Name),
                status);
        }

        return table;
    }

    /// <summary>Renders the final triage result with panels.</summary>
    public static void RenderResult(TriageResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]Triage Complete[/]").RuleStyle("green"));
        AnsiConsole.WriteLine();

        // Summary panel
        AnsiConsole.Write(new Panel(Markup.Escape(result.Summary))
            .Header("[bold blue]📋 Summary[/]")
            .Border(BoxBorder.Rounded)
            .Expand());
        AnsiConsole.WriteLine();

        // Risks panel
        var risksContent = result.Risks.Count > 0
            ? string.Join("\n", result.Risks.Select(r => $"⚠️  {Markup.Escape(r)}"))
            : "[green]No significant risks identified.[/]";
        AnsiConsole.Write(new Panel(risksContent)
            .Header("[bold yellow]⚠️ Risks[/]")
            .Border(BoxBorder.Rounded)
            .Expand());
        AnsiConsole.WriteLine();

        // Checklist panel
        var checklistContent = string.Join("\n", result.Checklist.Select(c => $"☐ {Markup.Escape(c)}"));
        AnsiConsole.Write(new Panel(checklistContent)
            .Header("[bold cyan]✅ Review Checklist[/]")
            .Border(BoxBorder.Rounded)
            .Expand());
        AnsiConsole.WriteLine();

        // PR Comment panel
        AnsiConsole.Write(new Panel(Markup.Escape(result.SuggestedPrCommentMarkdown))
            .Header("[bold magenta]💬 Suggested PR Comment (Markdown)[/]")
            .Border(BoxBorder.Double)
            .Expand());
    }

    /// <summary>Renders a friendly error panel.</summary>
    public static void RenderError(string message)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel($"[red]{Markup.Escape(message)}[/]")
            .Header("[bold red]❌ Error[/]")
            .Border(BoxBorder.Rounded)
            .Expand());
    }
}

/// <summary>
/// Progress reporter that displays streaming tokens as they arrive from LLM.
/// </summary>
public sealed class StreamingProgress
{
    private readonly StatusContext _statusContext;

    public StreamingProgress(StatusContext statusContext)
    {
        _statusContext = statusContext;
    }

    /// <summary>Updates the status display with the current step.</summary>
    public void ReportStep(TriageStep step, bool isComplete)
    {
        var status = isComplete ? "✅ Done" : "⏳ Working";
        _statusContext.Status($"{step.Emoji} {step.Name}: {status}");
    }

    /// <summary>Displays a token chunk as it arrives from the LLM.</summary>
    public void ReportToken(string token)
    {
        AnsiConsole.Markup(Markup.Escape(token));
    }

    /// <summary>Starts a new line for the next step.</summary>
    public void ReportNewLine()
    {
        AnsiConsole.WriteLine();
    }
}

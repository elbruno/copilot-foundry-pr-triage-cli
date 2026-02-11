using Microsoft.Extensions.Configuration;
using RepoTriage.Cli.Agents;
using RepoTriage.Cli.Models;
using RepoTriage.Cli.Ui;
using RepoTriage.Cli.Workflow;
using Spectre.Console;

// ─── Parse arguments ───────────────────────────────────────────
string? diffPath = null;
string? prUrl = null;
bool mock = false;
int? timeoutSeconds = null;
string? modelOverride = null;
bool noMenu = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--diff" when i + 1 < args.Length:
            diffPath = args[++i];
            break;
        case "--pr" when i + 1 < args.Length:
            prUrl = args[++i];
            break;
        case "--timeout" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var t) && t > 0)
                timeoutSeconds = t;
            break;
        case "--model" when i + 1 < args.Length:
            modelOverride = args[++i];
            break;
        case "--no-menu":
            noMenu = true;
            break;
        case "--mock":
            mock = true;
            break;
    }
}

if (diffPath is null && prUrl is null)
{
    AnsiConsole.MarkupLine("[red]Usage:[/] dotnet run -- --diff <path> [[--timeout <seconds>]] [[--model <name>]] [[--no-menu]] [[--mock]]");
    AnsiConsole.MarkupLine("       dotnet run -- --pr <github-pr-url> [[--timeout <seconds>]] [[--model <name>]] [[--no-menu]] [[--mock]]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[dim]Options:[/]");
    AnsiConsole.MarkupLine("  [cyan]--diff <path>[/]          Path to local diff/patch file");
    AnsiConsole.MarkupLine("  [cyan]--pr <url>[/]             GitHub PR URL (requires GITHUB_TOKEN)");
    AnsiConsole.MarkupLine("  [cyan]--timeout <seconds>[/]    HTTP timeout (default: 300)");
    AnsiConsole.MarkupLine("  [cyan]--model <name>[/]         Foundry Local model override (e.g., phi-3.5-mini, phi-4)");
    AnsiConsole.MarkupLine("  [cyan]--no-menu[/]             Skip interactive model/timeout selection");
    AnsiConsole.MarkupLine("  [cyan]--mock[/]                Use mock responses (skip LLM/GitHub calls)");
    return 1;
}

// ─── Render header ─────────────────────────────────────────────
ConsoleUi.RenderHeader();

// ─── Load configuration (env vars + user secrets) ──────────────
var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

// ─── Interactive menu for model and timeout selection ──────────
// Show menu if model/timeout not provided AND --no-menu not set
if (!noMenu && (modelOverride is null || timeoutSeconds is null))
{
    if (modelOverride is null)
    {
        modelOverride = StartupMenu.SelectModel();
    }
    
    if (timeoutSeconds is null)
    {
        timeoutSeconds = StartupMenu.SelectTimeout();
    }
    
    AnsiConsole.WriteLine();
}

// Apply defaults if still not set
timeoutSeconds ??= 300;

// ─── Build input ───────────────────────────────────────────────
PullRequestInput input;
var token = config["GITHUB_TOKEN"];

await using var copilotAgent = new CopilotAgentClient(token, mock);
await using var foundryAgent = new FoundryAgentClient(config, mock, timeoutSeconds.Value, modelOverride);

// Initialize both agents
await copilotAgent.InitializeAsync(CancellationToken.None);
await foundryAgent.InitializeAsync(CancellationToken.None);

AnsiConsole.MarkupLine($"[dim]Foundry Local endpoint:[/] {Markup.Escape(foundryAgent.Endpoint)}");
AnsiConsole.MarkupLine($"[dim]Foundry Local model:[/] {Markup.Escape(foundryAgent.Model)}");
AnsiConsole.WriteLine();

try
{
    if (prUrl is not null)
    {
        if (string.IsNullOrEmpty(token) && !mock)
        {
            ConsoleUi.RenderError(
                "GitHub PR mode requires GITHUB_TOKEN. Set it as an environment variable, in user secrets, or use --diff mode instead.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[dim]Fetching PR from:[/] {Markup.Escape(prUrl)}");
        input = await copilotAgent.GetPullRequestAsync(new Uri(prUrl), CancellationToken.None);
    }
    else
    {
        if (!File.Exists(diffPath))
        {
            ConsoleUi.RenderError($"Diff file not found: {diffPath}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[dim]Loading diff from:[/] {Markup.Escape(diffPath!)}");
        var diff = await File.ReadAllTextAsync(diffPath!);
        var files = ParseFilesFromDiff(diff);
        input = new PullRequestInput("Local Diff", "Loaded from local file", diff, files);
    }

    // ─── Run workflow ──────────────────────────────────────────
    var workflow = new TriageWorkflow(copilotAgent, foundryAgent);
    var result = await ConsoleUi.RunWithProgressAsync(workflow, input, CancellationToken.None);

    // ─── Render result ─────────────────────────────────────────
    ConsoleUi.RenderResult(result);
    return 0;
}
catch (Exception ex)
{
    ConsoleUi.RenderError(ex.Message);
    return 1;
}

// ─── Helpers ───────────────────────────────────────────────────
static List<string> ParseFilesFromDiff(string diff)
{
    return diff.Split('\n')
        .Where(line => line.StartsWith("+++ b/", StringComparison.Ordinal))
        .Select(line => line["+++ b/".Length..])
        .ToList();
}

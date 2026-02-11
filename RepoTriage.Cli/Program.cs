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
        case "--mock":
            mock = true;
            break;
    }
}

if (diffPath is null && prUrl is null)
{
    AnsiConsole.MarkupLine("[red]Usage:[/] dotnet run -- --diff <path> [[--mock]]");
    AnsiConsole.MarkupLine("       dotnet run -- --pr <github-pr-url> [[--mock]]");
    return 1;
}

// ─── Render header ─────────────────────────────────────────────
ConsoleUi.RenderHeader();

// ─── Load configuration (env vars + user secrets) ──────────────
var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

// ─── Build input ───────────────────────────────────────────────
PullRequestInput input;
var token = config["GITHUB_TOKEN"];

await using var copilotAgent = new CopilotAgentClient(token, mock);
using var foundryAgent = new FoundryAgentClient(config, mock);

// Initialize the Copilot agent
await copilotAgent.InitializeAsync(CancellationToken.None);

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

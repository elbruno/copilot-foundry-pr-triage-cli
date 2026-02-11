using Spectre.Console;

namespace RepoTriage.Cli.Ui;

/// <summary>
/// Interactive startup menu for selecting Foundry Local model and timeout configuration.
/// Uses Spectre.Console's SelectionPrompt for user-friendly CLI interaction.
/// </summary>
public static class StartupMenu
{
    /// <summary>
    /// Prompts user to select a Foundry Local model.
    /// </summary>
    /// <returns>The selected model name/alias.</returns>
    public static string SelectModel()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Non-interactive terminal detected. Using default model: phi-4[/]");
            return "phi-4";
        }

        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold cyan]Select Foundry Local Model:[/]")
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to reveal more models)[/]")
                .AddChoices(new[]
                {
                    "phi-3.5-mini (recommended for speed)",
                    "phi-4 (default, more capable)",
                    "Phi-4-trtrtx-gpu:1 (GPU-optimized)",
                    "Custom (enter model name)"
                }));

        // Parse the selected choice
        if (choice.StartsWith("phi-3.5-mini", StringComparison.OrdinalIgnoreCase))
            return "phi-3.5-mini";
        if (choice.StartsWith("phi-4 (default", StringComparison.OrdinalIgnoreCase))
            return "phi-4";
        if (choice.StartsWith("Phi-4-trtrtx-gpu", StringComparison.OrdinalIgnoreCase))
            return "Phi-4-trtrtx-gpu:1";
        
        // Custom model - prompt for input
        AnsiConsole.WriteLine();
        var customModel = AnsiConsole.Ask<string>("[cyan]Enter custom model name:[/]");
        return customModel;
    }

    /// <summary>
    /// Prompts user to select a timeout duration in seconds.
    /// </summary>
    /// <returns>The selected timeout in seconds.</returns>
    public static int SelectTimeout()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️  Non-interactive terminal detected. Using default timeout: 300 seconds[/]");
            return 300;
        }

        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold cyan]Select HTTP Timeout:[/]")
                .PageSize(10)
                .AddChoices(new[]
                {
                    "60 seconds (1 minute)",
                    "120 seconds (2 minutes)",
                    "300 seconds (5 minutes - default)",
                    "600 seconds (10 minutes)",
                    "900 seconds (15 minutes)"
                }));

        // Parse the selected choice - extract the number
        var parts = choice.Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0], out var timeout))
            return timeout;
        
        // Default fallback
        return 300;
    }

    /// <summary>
    /// Checks if the --no-menu flag is present in the arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>True if --no-menu flag is present, false otherwise.</returns>
    public static bool HasNoMenuFlag(string[] args)
    {
        return args.Any(arg => arg.Equals("--no-menu", StringComparison.OrdinalIgnoreCase));
    }
}

# Copilot Instructions for RepoTriage.Cli

A .NET 10 console app demonstrating multi-agent orchestration with Microsoft Agent Framework. Two agents work together: **Copilot Agent** (GitHub PR operations) and **Foundry Local Agent** (LLM analysis via local phi-4 model).

---

## Architecture Overview

```
Program.cs                    → CLI entry point, args parsing, DI setup
Workflow/TriageWorkflow.cs    → 5-step orchestration: fetch → summarize → risks → checklist → comment
Agents/FoundryAgentClient.cs  → ChatClientAgent + IChatClient → Foundry Local (OpenAI-compatible)
Agents/CopilotAgentClient.cs  → CopilotClient.AsAIAgent() for GitHub operations
Ui/ConsoleUi.cs               → Spectre.Console live tables, panels, streaming progress
```

**Data flow:** `PullRequestInput` → `TriageWorkflow.RunAsync()` → `TriageResult`

---

## Workflow Steps (5-Step Pipeline)

| #   | Agent      | Step                         | Method                                        |
| --- | ---------- | ---------------------------- | --------------------------------------------- |
| 1   | 🤖 Copilot | Fetch PR context / Load diff | `GetDiffAsync()`                              |
| 2   | 🧠 Foundry | Summarize the change set     | `CompleteAsync(Prompts.SummarizeSystem, ...)` |
| 3   | 🧠 Foundry | Identify risks               | `CompleteAsync(Prompts.RisksSystem, ...)`     |
| 4   | 🧠 Foundry | Generate review checklist    | `CompleteAsync(Prompts.ChecklistSystem, ...)` |
| 5   | 🤖 Copilot | Draft PR comment (Markdown)  | `DraftCommentAsync()`                         |

---

## Key Patterns (Follow These)

### Agent Implementation

```csharp
// Agents implement IAsyncDisposable and require initialization
public interface IFoundryAgentClient : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
    IAsyncEnumerable<string> CompleteStreamingAsync(...);
}

// Foundry Agent: ChatClientAgent wrapping IChatClient (OpenAI SDK → Foundry Local)
var openAiClient = new OpenAIClient(credential, new() { Endpoint = new(_endpoint) });
_chatClient = openAiClient.GetChatClient(_model).AsIChatClient();
var agent = new ChatClientAgent(_chatClient, instructions: systemPrompt, name: "MyAgent");
await foreach (var update in agent.RunStreamingAsync(prompt, ct)) { ... }

// Copilot Agent: CopilotClient.AsAIAgent() for GitHub-specific tools
```

### Agent Lifecycle

```csharp
// Always use await using for proper disposal
await using var copilotAgent = new CopilotAgentClient(token, mock);
await using var foundryAgent = new FoundryAgentClient(config, mock, timeoutSeconds);

// Initialize before use
await copilotAgent.InitializeAsync(ct);
await foundryAgent.InitializeAsync(ct);
```

### Streaming vs Non-Streaming

- Use `RunStreamingAsync()` for live demos (token-by-token display)
- Use `CompleteAsync()` internally if accumulating full response
- Always support `CancellationToken` for long-running LLM calls

### Retry Logic

Exponential backoff (2s, 4s, 8s) required for LLM calls — see `TriageWorkflow.RetryAsync<T>()`

### Error Handling

```csharp
// Wrap workflow in try-catch, render errors via ConsoleUi
try {
    var result = await workflow.RunAsync(input, progress, ct);
    ConsoleUi.RenderResult(result);
} catch (Exception ex) {
    ConsoleUi.RenderError(ex.Message);  // User-friendly panel
    return 1;
}
```

---

## CLI Arguments

| Arg               | Purpose                                         |
| ----------------- | ----------------------------------------------- |
| `--diff <path>`   | Local patch file                                |
| `--pr <url>`      | GitHub PR URL (requires `GITHUB_TOKEN`)         |
| `--timeout <sec>` | HTTP timeout (default: 300)                     |
| `--model <name>`  | Model override (e.g., `phi-3.5-mini`)           |
| `--no-menu`       | Skip interactive model/timeout selection        |
| `--mock`          | Skip LLM/GitHub calls, use deterministic output |

### Planned: Interactive Model Selection

When `--model` and `--timeout` are not specified (and `--no-menu` is absent), show Spectre.Console `SelectionPrompt` for:

- Model: `phi-3.5-mini` (fast), `phi-4`, `Phi-4-trtrtx-gpu:1`, or custom
- Timeout: 60, 120, 300 (default), 600, 900 seconds

See `docs/plans/plan_260211_1153.md` for implementation details.

---

## Documentation Rules

- All docs in `docs/` except `README.md` and `LICENSE` at root
- Plans: `docs/plans/plan_YYMMDD_HHMM.md` (e.g., `plan_260211_1030.md`)

---

## UI Conventions (Spectre.Console)

- `AnsiConsole.Live()` with `Table` for step progress
- `Panel` with `BoxBorder.Rounded` for results
- `Markup.Escape()` all user content
- `StreamingProgress` class for token-by-token display

---

## Configuration (env vars or .NET User Secrets)

| Variable                 | Default                 | Note                    |
| ------------------------ | ----------------------- | ----------------------- |
| `FOUNDRY_LOCAL_ENDPOINT` | `http://localhost:5273` | Port changes on restart |
| `FOUNDRY_LOCAL_MODEL`    | `phi-4`                 | Smaller: `phi-3.5-mini` |
| `GITHUB_TOKEN`           | _(required for --pr)_   | PAT with repo read      |

---

## Build & Run

```bash
dotnet build
dotnet run --project RepoTriage.Cli -- --diff docs/sample.diff.patch --mock  # Quick test
foundry model run phi-4  # Start LLM first
dotnet run --project RepoTriage.Cli -- --pr https://github.com/owner/repo/pull/123
```

---

## Azure Rules

- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

---

## References

- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/)
- [ChatClientAgent Pattern](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/chat-client-agent?pivots=programming-language-csharp)
- [GitHub Copilot Agents](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/github-copilot-agent?pivots=programming-language-csharp)

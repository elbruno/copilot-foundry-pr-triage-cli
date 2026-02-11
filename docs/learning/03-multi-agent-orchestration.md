# Learning Module 03: Multi-Agent Orchestration

## Introduction

Real-world AI applications often require **multiple specialized agents** working together to accomplish complex tasks. This module explores how the Repo Triage Agent orchestrates two distinct agents (GitHub Copilot Agent and Foundry Local Agent) through the `TriageWorkflow` coordinator.

---

## Why Multi-Agent Orchestration?

### The Problem: Single Agent Limitations

A single agent trying to do everything faces challenges:

```csharp
// ❌ One agent for all tasks
var response = await universalAgent.RunAsync(
    "Fetch PR #123, summarize it, identify risks, generate a checklist, and draft a comment");
```

**Problems:**
- **Complexity** — Agent must understand many different operations
- **Error-prone** — One failure breaks the entire flow
- **Poor separation of concerns** — Mixing GitHub API logic with LLM analysis
- **Inflexible** — Hard to swap components or add new steps

### The Solution: Specialized Agent Orchestration

Divide responsibilities among specialized agents coordinated by an orchestrator:

```csharp
// ✅ Specialized agents with orchestration
var prContext = await copilotAgent.FetchContextAsync(prUrl);       // GitHub operations
var summary = await foundryAgent.SummarizeAsync(prContext.Diff);   // LLM analysis
var risks = await foundryAgent.IdentifyRisksAsync(prContext.Diff); // LLM analysis
var checklist = await foundryAgent.GenerateChecklistAsync(prContext.Diff); // LLM analysis
var comment = await copilotAgent.DraftCommentAsync(summary, risks, checklist); // Formatting
```

**Benefits:**
- **Single responsibility** — Each agent has one job
- **Independent failure handling** — Retry/fallback per agent
- **Testability** — Mock individual agents in isolation
- **Flexibility** — Swap agent implementations without changing workflow

---

## Orchestration Pattern

### Workflow Coordinator

The `TriageWorkflow` class acts as the **orchestrator**, managing:

1. **Agent coordination** — Calling agents in the correct sequence
2. **Data flow** — Passing outputs from one agent to the next
3. **Error handling** — Retry logic and graceful degradation
4. **Progress tracking** — UI updates for each step

### Architecture

```
┌──────────────────────────────────────────────┐
│           User / Program.cs                  │
└────────────────┬─────────────────────────────┘
                 │
                 ↓
┌──────────────────────────────────────────────┐
│         TriageWorkflow (Orchestrator)        │
│  • Coordinates agent execution               │
│  • Manages data flow between agents          │
│  • Handles errors and retries                │
└────────┬─────────────────────┬────────────────┘
         │                     │
         ↓                     ↓
┌─────────────────┐   ┌──────────────────────┐
│ GitHub Copilot  │   │ Foundry Local Agent  │
│ Agent           │   │ (ChatClientAgent)    │
│                 │   │                      │
│ • Fetch PR      │   │ • Summarize          │
│ • Draft comment │   │ • Identify risks     │
│                 │   │ • Generate checklist │
└─────────────────┘   └──────────────────────┘
```

---

## Implementation: TriageWorkflow

### Core Structure

```csharp
public class TriageWorkflow
{
    private readonly ICopilotAgentClient _copilotAgent;
    private readonly IFoundryAgentClient _foundryAgent;
    private readonly IConsoleUi _ui;
    private readonly ILogger<TriageWorkflow> _logger;

    public TriageWorkflow(
        ICopilotAgentClient copilotAgent,
        IFoundryAgentClient foundryAgent,
        IConsoleUi ui,
        ILogger<TriageWorkflow> logger)
    {
        _copilotAgent = copilotAgent;
        _foundryAgent = foundryAgent;
        _ui = ui;
        _logger = logger;
    }

    public async Task<TriageResult> ExecuteAsync(PullRequestInput input)
    {
        try
        {
            // Step 1: Fetch context (GitHub Copilot Agent)
            _ui.ShowStep(1, "🤖 Copilot Agent", "Fetching PR context...");
            var prContext = await FetchContextWithRetryAsync(input);

            // Step 2: Summarize (Foundry Local Agent)
            _ui.ShowStep(2, "🧠 Foundry Local Agent", "Summarizing changes...");
            var summary = await SummarizeWithRetryAsync(prContext.Diff);

            // Step 3: Identify risks (Foundry Local Agent)
            _ui.ShowStep(3, "🧠 Foundry Local Agent", "Identifying risks...");
            var risks = await IdentifyRisksWithRetryAsync(prContext.Diff);

            // Step 4: Generate checklist (Foundry Local Agent)
            _ui.ShowStep(4, "🧠 Foundry Local Agent", "Generating checklist...");
            var checklist = await GenerateChecklistWithRetryAsync(prContext.Diff);

            // Step 5: Draft comment (GitHub Copilot Agent)
            _ui.ShowStep(5, "🤖 Copilot Agent", "Drafting PR comment...");
            var comment = await DraftCommentWithRetryAsync(summary, risks, checklist);

            return new TriageResult
            {
                Summary = summary,
                Risks = risks,
                ReviewChecklist = checklist,
                SuggestedComment = comment
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed");
            throw new TriageWorkflowException("Failed to complete triage workflow", ex);
        }
    }
}
```

### Step-by-Step Execution

#### Step 1: Fetch PR Context

```csharp
private async Task<PrContext> FetchContextWithRetryAsync(PullRequestInput input)
{
    const int maxRetries = 3;
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            if (input.PrUrl != null)
            {
                return await _copilotAgent.FetchContextAsync(input.PrUrl);
            }
            else if (input.DiffFilePath != null)
            {
                var diffContent = await File.ReadAllTextAsync(input.DiffFilePath);
                return new PrContext { Diff = diffContent };
            }
        }
        catch (HttpRequestException ex) when (i < maxRetries - 1)
        {
            _logger.LogWarning(ex, $"Fetch failed, retry {i + 1}/{maxRetries}");
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // Exponential backoff
        }
    }
    
    throw new Exception("Failed to fetch PR context after retries");
}
```

#### Step 2-4: LLM Analysis (Parallel Execution)

For better performance, run independent LLM tasks concurrently:

```csharp
public async Task<TriageResult> ExecuteAsync(PullRequestInput input)
{
    // Step 1: Fetch context (sequential)
    var prContext = await FetchContextWithRetryAsync(input);

    // Steps 2-4: Parallel execution (all use same diff content)
    var summaryTask = SummarizeWithRetryAsync(prContext.Diff);
    var risksTask = IdentifyRisksWithRetryAsync(prContext.Diff);
    var checklistTask = GenerateChecklistWithRetryAsync(prContext.Diff);

    // Wait for all to complete
    await Task.WhenAll(summaryTask, risksTask, checklistTask);

    var summary = await summaryTask;
    var risks = await risksTask;
    var checklist = await checklistTask;

    // Step 5: Draft comment (sequential, depends on previous results)
    var comment = await DraftCommentWithRetryAsync(summary, risks, checklist);

    return new TriageResult { ... };
}
```

**Performance gain:** ~3x faster (10s vs 30s for sequential execution)

---

## Error Handling Strategies

### 1. Retry with Exponential Backoff

```csharp
private async Task<string> SummarizeWithRetryAsync(string diffContent)
{
    const int maxRetries = 3;
    var delays = new[] { 2, 4, 8 }; // Exponential backoff in seconds

    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await _foundryAgent.SummarizeAsync(diffContent);
        }
        catch (HttpRequestException ex) when (i < maxRetries - 1)
        {
            _logger.LogWarning(ex, $"LLM request failed, retry {i + 1}/{maxRetries}");
            await Task.Delay(TimeSpan.FromSeconds(delays[i]));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during summarization");
            throw;
        }
    }

    throw new Exception("LLM summarization failed after retries");
}
```

### 2. Graceful Degradation

```csharp
private async Task<string> IdentifyRisksWithRetryAsync(string diffContent)
{
    try
    {
        return await IdentifyRisksWithRetryAsync(diffContent);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Risk identification failed, using fallback");
        
        // Fallback: Return generic risks based on diff size
        return "⚠️ Risk analysis unavailable. Manual review recommended.";
    }
}
```

### 3. Timeout Protection

```csharp
private async Task<string> SummarizeAsync(string diffContent)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    
    try
    {
        return await _foundryAgent.SummarizeAsync(diffContent, cts.Token);
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Summarization timed out after 60 seconds");
        throw new TimeoutException("LLM request timed out");
    }
}
```

---

## Streaming Progress Updates

For better UX, stream agent output to the console:

### Implementation

```csharp
public async Task<TriageResult> ExecuteStreamingAsync(PullRequestInput input)
{
    // Step 1: Fetch context (non-streaming)
    var prContext = await FetchContextWithRetryAsync(input);

    // Step 2: Summarize with streaming
    _ui.ShowStep(2, "🧠 Foundry Local Agent", "Summarizing changes...");
    var summaryBuilder = new StringBuilder();
    
    await foreach (var token in _foundryAgent.SummarizeStreamingAsync(prContext.Diff))
    {
        Console.Write(token); // Display token immediately
        summaryBuilder.Append(token);
    }
    
    var summary = summaryBuilder.ToString();
    Console.WriteLine("\n"); // End of stream

    // Similar for steps 3-4 with streaming

    // Step 5: Draft comment
    var comment = await DraftCommentWithRetryAsync(summary, risks, checklist);

    return new TriageResult { ... };
}
```

### UI Integration with Spectre.Console

```csharp
public async Task<TriageResult> ExecuteStreamingAsync(PullRequestInput input)
{
    await AnsiConsole.Status()
        .StartAsync("Processing...", async ctx =>
        {
            // Step 2: Summarize with live rendering
            ctx.Status("🧠 Summarizing changes...");
            
            var liveDisplay = AnsiConsole.Live(new Panel(""));
            await liveDisplay.StartAsync(async liveCtx =>
            {
                var summaryBuilder = new StringBuilder();
                
                await foreach (var token in _foundryAgent.SummarizeStreamingAsync(prContext.Diff))
                {
                    summaryBuilder.Append(token);
                    liveCtx.UpdateTarget(new Panel(summaryBuilder.ToString())
                        .Header("Summary"));
                    await Task.Delay(10); // Smooth rendering
                }
            });
        });
}
```

---

## Agent Communication Patterns

### 1. Sequential Pipeline

Each step depends on the previous step's output:

```
Step 1 → output1 → Step 2 → output2 → Step 3 → output3
```

**Example:** Fetch PR → Summarize → Identify risks in summary → Generate checklist from risks

```csharp
var prContext = await _copilotAgent.FetchContextAsync(prUrl);
var summary = await _foundryAgent.SummarizeAsync(prContext.Diff);
var risks = await _foundryAgent.IdentifyRisksAsync(summary); // Uses summary, not diff
var checklist = await _foundryAgent.GenerateChecklistAsync(risks); // Uses risks
```

### 2. Parallel Fan-Out

Multiple independent operations on the same input:

```
         ┌→ Step 2a (Summarize)
Input → ─┼→ Step 2b (Identify Risks)
         └→ Step 2c (Generate Checklist)
```

**Example:** Analyze diff from three perspectives simultaneously

```csharp
var summaryTask = _foundryAgent.SummarizeAsync(diffContent);
var risksTask = _foundryAgent.IdentifyRisksAsync(diffContent);
var checklistTask = _foundryAgent.GenerateChecklistAsync(diffContent);

await Task.WhenAll(summaryTask, risksTask, checklistTask);
```

### 3. Conditional Branching

Different agents based on input type:

```csharp
PrContext prContext;

if (input.PrUrl != null)
{
    // Use GitHub Copilot Agent for live PR data
    prContext = await _copilotAgent.FetchContextAsync(input.PrUrl);
}
else if (input.DiffFilePath != null)
{
    // Use file I/O for local diffs
    var diffContent = await File.ReadAllTextAsync(input.DiffFilePath);
    prContext = new PrContext { Diff = diffContent };
}
else
{
    throw new ArgumentException("Must provide either PR URL or diff file path");
}
```

### 4. Aggregation

Combine outputs from multiple agents:

```csharp
var summary = await summaryTask;
var risks = await risksTask;
var checklist = await checklistTask;

// Aggregate into final result
var comment = await _copilotAgent.DraftCommentAsync(
    summary: summary,
    risks: string.Join("\n", risks),
    checklist: string.Join("\n", checklist));
```

---

## Testing Multi-Agent Workflows

### 1. Unit Test with Mock Agents

```csharp
public class TriageWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_ValidInput_ReturnsCompleteResult()
    {
        // Arrange
        var mockCopilotAgent = new Mock<ICopilotAgentClient>();
        mockCopilotAgent
            .Setup(x => x.FetchContextAsync(It.IsAny<string>()))
            .ReturnsAsync(new PrContext { Diff = "sample diff" });

        var mockFoundryAgent = new Mock<IFoundryAgentClient>();
        mockFoundryAgent
            .Setup(x => x.SummarizeAsync(It.IsAny<string>()))
            .ReturnsAsync("Summary");
        mockFoundryAgent
            .Setup(x => x.IdentifyRisksAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "Risk 1" });

        var workflow = new TriageWorkflow(
            mockCopilotAgent.Object,
            mockFoundryAgent.Object,
            Mock.Of<IConsoleUi>(),
            Mock.Of<ILogger<TriageWorkflow>>());

        var input = new PullRequestInput { PrUrl = "https://github.com/owner/repo/pull/123" };

        // Act
        var result = await workflow.ExecuteAsync(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Summary", result.Summary);
        Assert.Single(result.Risks);
    }
}
```

### 2. Integration Test with Real Agents

```csharp
[Fact]
public async Task ExecuteAsync_RealAgents_CompletesSuccessfully()
{
    // Arrange
    var chatClient = new OpenAIClient(
        new Uri("http://localhost:5272/v1"),
        new ApiKeyCredential("test"))
        .AsChatClient("phi-4");

    var foundryAgent = new FoundryAgentClient(chatClient);
    var copilotAgent = new CopilotAgentClient(); // Uses mock mode

    var workflow = new TriageWorkflow(
        copilotAgent,
        foundryAgent,
        new ConsoleUi(),
        NullLogger<TriageWorkflow>.Instance);

    var input = new PullRequestInput { DiffFilePath = "docs/sample.diff.patch" };

    // Act
    var result = await workflow.ExecuteAsync(input);

    // Assert
    Assert.NotNull(result);
    Assert.NotEmpty(result.Summary);
    Assert.NotEmpty(result.Risks);
    Assert.NotEmpty(result.ReviewChecklist);
}
```

---

## Performance Optimization

### 1. Parallel Execution

```csharp
// ❌ Sequential (30 seconds total)
var summary = await SummarizeAsync(diff);      // 10s
var risks = await IdentifyRisksAsync(diff);    // 10s
var checklist = await GenerateChecklistAsync(diff); // 10s

// ✅ Parallel (10 seconds total)
var tasks = Task.WhenAll(
    SummarizeAsync(diff),
    IdentifyRisksAsync(diff),
    GenerateChecklistAsync(diff));
var results = await tasks;
```

### 2. Caching

```csharp
private readonly Dictionary<string, PrContext> _prContextCache = new();

private async Task<PrContext> FetchContextWithCacheAsync(string prUrl)
{
    if (_prContextCache.TryGetValue(prUrl, out var cached))
    {
        _logger.LogInformation("Using cached PR context");
        return cached;
    }

    var context = await _copilotAgent.FetchContextAsync(prUrl);
    _prContextCache[prUrl] = context;
    return context;
}
```

### 3. Streaming for Perceived Performance

```csharp
// ✅ User sees output immediately as it's generated
await foreach (var token in _foundryAgent.SummarizeStreamingAsync(diff))
{
    Console.Write(token); // Display token-by-token
}

// vs.

// ❌ User waits for full response before seeing anything
var summary = await _foundryAgent.SummarizeAsync(diff);
Console.WriteLine(summary);
```

---

## Key Takeaways

1. **Multi-agent orchestration separates concerns** (GitHub ops vs. LLM analysis)
2. **Workflow coordinator manages execution flow** and error handling
3. **Parallel execution improves performance** for independent tasks (3x speedup)
4. **Retry logic with exponential backoff** handles transient failures
5. **Streaming provides better UX** by displaying output as it's generated
6. **Mock agents enable unit testing** of workflow logic
7. **Specialized agents are easier to maintain** than monolithic agents

---

## Practical Exercise

### Task: Add a New Workflow Step

Add a code style check as Step 4 (shifting current Step 4 to Step 5):

**Current workflow:**
1. Fetch context
2. Summarize
3. Identify risks
4. Generate checklist
5. Draft comment

**New workflow:**
1. Fetch context
2. Summarize
3. Identify risks
4. **Check code style** ← NEW
5. Generate checklist
6. Draft comment

**Steps:**
1. Create `CheckStyleAsync()` method in `FoundryAgentClient`
2. Update `TriageWorkflow.ExecuteAsync()` to call this method
3. Modify `TriageResult` to include `StyleViolations` property
4. Update `ConsoleUi.Render()` to display style violations

**Bonus:** Make steps 2-4 run in parallel since they're independent.

---

## Next Steps

- **Review Code:** Explore the actual `TriageWorkflow.cs` implementation
- **Experiment:** Add custom workflow steps (e.g., sentiment analysis, test coverage check)
- **Extend:** Integrate with CI/CD to automatically triage PRs

---

## References

- [Microsoft Agent Framework Documentation](https://learn.microsoft.com/en-us/agent-framework/)
- [Orchestration Patterns in AI Applications](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/orchestration-patterns)
- [Task Parallel Library (TPL)](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl)

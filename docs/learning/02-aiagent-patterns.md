# Learning Module 02: AIAgent Patterns

## Introduction

The [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/) provides the `AIAgent` pattern for building structured, production-ready AI agents. This module explores two key agent types used in the Repo Triage Agent:

1. **`ChatClientAgent`** — Wraps `IChatClient` with instructions and orchestration
2. **`GitHubCopilotAgent`** — Integrates with GitHub Copilot CLI and provides tool/function calling

---

## Why AIAgent?

### The Problem: Unstructured LLM Interactions

Using `IChatClient` directly is powerful but requires boilerplate for common patterns:

```csharp
// ❌ Manual orchestration for every call
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, instructions),
    new ChatMessage(ChatRole.User, userPrompt)
};
var response = await chatClient.CompleteAsync(messages);
var result = response.Message.Text;
```

**Repeated challenges:**
- Managing conversation history (multi-turn context)
- Adding tool/function calling capabilities
- Implementing retry logic and error handling
- Streaming responses with proper state management

### The Solution: AIAgent Pattern

`AIAgent` encapsulates these concerns in a reusable abstraction:

```csharp
// ✅ Declarative agent configuration
AIAgent agent = new ChatClientAgent(
    chatClient: foundryClient,
    instructions: "You summarize code changes into concise bullet points.",
    name: "SummarizerAgent");

// ✅ Simple execution
AgentResponse response = await agent.RunAsync("Summarize this diff: ...");
Console.WriteLine(response.GetTextContent());
```

**Benefits:**
- **Declarative** — Define agent behavior upfront
- **Reusable** — Same agent instance for multiple calls
- **Streaming** — Built-in support for `RunStreamingAsync()`
- **Session management** — Automatic conversation history tracking
- **Error handling** — Retry logic and graceful degradation

---

## Pattern 1: ChatClientAgent

### What is ChatClientAgent?

[`ChatClientAgent`](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/chat-client-agent?pivots=programming-language-csharp) wraps `IChatClient` with:

- **Persistent instructions** — System prompt applied to every request
- **Session management** — Conversation history for multi-turn interactions
- **Streaming support** — Token-by-token responses
- **Response parsing** — Structured output extraction

### Basic Usage

```csharp
using Microsoft.Agents.AI;

// 1. Create IChatClient (OpenAI, Azure, Foundry Local, etc.)
IChatClient chatClient = new OpenAIClient(
    new Uri("http://localhost:5272/v1"),
    new ApiKeyCredential("not-needed"))
    .AsChatClient("phi-4");

// 2. Create ChatClientAgent with instructions
AIAgent agent = new ChatClientAgent(
    chatClient: chatClient,
    instructions: "You are an expert code reviewer. Identify security vulnerabilities.",
    name: "SecurityAnalyzer");

// 3. Run the agent
AgentResponse response = await agent.RunAsync(
    prompt: "Review this code:\n\n" + codeSnippet);

// 4. Extract result
string analysis = response.GetTextContent();
Console.WriteLine(analysis);
```

### Streaming Execution

For better UX, display tokens as they arrive:

```csharp
await foreach (var update in agent.RunStreamingAsync(prompt))
{
    if (update.Type == AgentResponseType.Text)
    {
        Console.Write(update.GetTextContent());
    }
}
```

### Multi-Turn Conversations

Maintain context across multiple requests:

```csharp
var sessionId = Guid.NewGuid().ToString();

// First turn
var response1 = await agent.RunAsync(
    prompt: "Summarize this diff: ...",
    sessionId: sessionId);

// Second turn (uses previous context)
var response2 = await agent.RunAsync(
    prompt: "Based on the summary, what are the security risks?",
    sessionId: sessionId);
```

---

## Pattern 2: GitHub Copilot Agent

### What is GitHubCopilotAgent?

[`GitHubCopilotAgent`](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/github-copilot-agent?pivots=programming-language-csharp) integrates with the GitHub Copilot CLI to provide:

- **GitHub authentication** — Uses existing Copilot session
- **Tool/function calling** — Define custom operations for the agent
- **GitHub API integration** — Built-in helpers for PR, issue, repo operations
- **Permission management** — User consent for shell/file access

### Basic Usage

```csharp
using Microsoft.Agents.AI.GitHub.Copilot;

// 1. Create CopilotClient
await using CopilotClient copilotClient = new();
await copilotClient.StartAsync(); // Connects to Copilot CLI

// 2. Define tools (functions the agent can call)
var fetchPrTool = AIFunction.Create(
    name: "fetch_pull_request",
    description: "Fetches PR metadata and diff content",
    parameters: new { owner = "", repo = "", prNumber = 0 },
    implementation: async (string owner, string repo, int prNumber) =>
    {
        // Call GitHub API
        return prData;
    });

// 3. Create agent with tools
AIAgent agent = copilotClient.AsAIAgent(
    tools: [fetchPrTool],
    instructions: "You are a PR triage assistant. Fetch PR data when requested.");

// 4. Run the agent (it can call fetchPrTool internally)
AgentResponse response = await agent.RunAsync(
    prompt: "Analyze PR #123 in microsoft/foundry-local");
```

### Tool/Function Calling Flow

```
User: "Analyze PR #123"
      ↓
Agent (LLM): "I need to fetch PR data"
      ↓ [calls fetchPrTool(owner="microsoft", repo="foundry-local", prNumber=123)]
Tool: Returns PR data
      ↓
Agent (LLM): "Based on the data, here's my analysis: ..."
      ↓
User: Receives final response
```

### Defining Tools

```csharp
// Simple tool with parameters
var searchCodeTool = AIFunction.Create(
    name: "search_code",
    description: "Search codebase for a pattern",
    parameters: new { pattern = "" },
    implementation: async (string pattern) =>
    {
        // Run grep or search logic
        return searchResults;
    });

// Tool with multiple parameters
var createIssueTool = AIFunction.Create(
    name: "create_issue",
    description: "Create a GitHub issue",
    parameters: new { title = "", body = "", labels = new[] { "" } },
    implementation: async (string title, string body, string[] labels) =>
    {
        // Call GitHub API to create issue
        return issueUrl;
    });
```

---

## Comparison: ChatClientAgent vs. GitHubCopilotAgent

| Feature | ChatClientAgent | GitHubCopilotAgent |
|---------|-----------------|-------------------|
| **Backend** | Any `IChatClient` provider | GitHub Copilot CLI session |
| **Authentication** | API key / credential | Copilot CLI login |
| **Tool Calling** | ❌ No (planned) | ✅ Yes (`AIFunction`) |
| **GitHub Integration** | ❌ Manual API calls | ✅ Built-in helpers |
| **Use Case** | General LLM tasks | GitHub-specific operations |
| **Streaming** | ✅ Yes | ✅ Yes |
| **Session Management** | ✅ Yes | ✅ Yes |

---

## Implementation in Repo Triage Agent

### Foundry Local Agent (ChatClientAgent)

```csharp
public class FoundryAgentClient : IFoundryAgentClient
{
    private readonly AIAgent _summarizerAgent;
    private readonly AIAgent _riskAnalyzerAgent;
    private readonly AIAgent _checklistGeneratorAgent;

    public FoundryAgentClient(IChatClient chatClient)
    {
        _summarizerAgent = new ChatClientAgent(
            chatClient,
            instructions: "You summarize code changes into 3-5 concise bullet points.",
            name: "Summarizer");

        _riskAnalyzerAgent = new ChatClientAgent(
            chatClient,
            instructions: "You identify security, performance, and breaking changes in code diffs.",
            name: "RiskAnalyzer");

        _checklistGeneratorAgent = new ChatClientAgent(
            chatClient,
            instructions: "You generate review checklist items for code reviewers.",
            name: "ChecklistGenerator");
    }

    public async Task<string> SummarizeAsync(string diffContent)
    {
        var response = await _summarizerAgent.RunAsync(
            $"Summarize these changes:\n\n{diffContent}");
        return response.GetTextContent();
    }

    public async IAsyncEnumerable<string> SummarizeStreamingAsync(string diffContent)
    {
        await foreach (var update in _summarizerAgent.RunStreamingAsync(
            $"Summarize these changes:\n\n{diffContent}"))
        {
            if (update.Type == AgentResponseType.Text)
                yield return update.GetTextContent();
        }
    }

    // Similar for IdentifyRisksAsync and GenerateChecklistAsync
}
```

### GitHub Copilot Agent (Tool-Based)

```csharp
public class CopilotAgentClient : ICopilotAgentClient
{
    private readonly CopilotClient _copilotClient;
    private AIAgent _agent;

    public CopilotAgentClient()
    {
        _copilotClient = new CopilotClient();
    }

    public async Task InitializeAsync()
    {
        await _copilotClient.StartAsync();

        // Define tools
        var fetchPrTool = AIFunction.Create(
            name: "fetch_pr",
            description: "Fetch PR metadata and diff",
            parameters: new { prUrl = "" },
            implementation: FetchPrAsync);

        var draftCommentTool = AIFunction.Create(
            name: "draft_comment",
            description: "Format review comment as Markdown",
            parameters: new { summary = "", risks = "", checklist = "" },
            implementation: DraftCommentAsync);

        // Create agent with tools
        _agent = _copilotClient.AsAIAgent(
            tools: [fetchPrTool, draftCommentTool],
            instructions: "You are a PR triage assistant. Use tools to fetch data and draft comments.");
    }

    private async Task<string> FetchPrAsync(string prUrl)
    {
        // Parse URL and call GitHub API
        // Return PR data + diff
    }

    private async Task<string> DraftCommentAsync(string summary, string risks, string checklist)
    {
        // Format as Markdown
        return $"## Summary\n{summary}\n\n## Risks\n{risks}\n\n## Checklist\n{checklist}";
    }

    public async Task<PrContext> FetchContextAsync(string prUrl)
    {
        var response = await _agent.RunAsync($"Fetch PR context for {prUrl}");
        // Parse response
    }
}
```

---

## Advanced Patterns

### 1. Agent Chaining

Run multiple agents in sequence, passing output from one to the next:

```csharp
// Step 1: Summarize
var summary = await summarizerAgent.RunAsync(diffContent);

// Step 2: Analyze risks based on summary
var sessionId = Guid.NewGuid().ToString();
await riskAnalyzerAgent.RunAsync(
    $"Based on this summary, identify risks:\n\n{summary.GetTextContent()}",
    sessionId: sessionId);

// Step 3: Ask follow-up question in same session
await riskAnalyzerAgent.RunAsync(
    "Are there any security vulnerabilities?",
    sessionId: sessionId);
```

### 2. Parallel Agent Execution

Run independent agents concurrently:

```csharp
var summaryTask = summarizerAgent.RunAsync(diffContent);
var risksTask = riskAnalyzerAgent.RunAsync(diffContent);
var checklistTask = checklistGeneratorAgent.RunAsync(diffContent);

await Task.WhenAll(summaryTask, risksTask, checklistTask);

var summary = summaryTask.Result.GetTextContent();
var risks = risksTask.Result.GetTextContent();
var checklist = checklistTask.Result.GetTextContent();
```

**Performance gain:** 3x faster than sequential execution (assuming sufficient GPU memory)

### 3. Agent with Custom Response Parser

Extract structured data from agent responses:

```csharp
public class RiskItem
{
    public string Category { get; set; } // "Security", "Performance", "Breaking"
    public string Description { get; set; }
    public string Severity { get; set; } // "Low", "Medium", "High"
}

public async Task<List<RiskItem>> ParseRisksAsync(AgentResponse response)
{
    var text = response.GetTextContent();
    // Parse text into structured RiskItem objects
    // (use regex, JSON parsing, or another LLM call)
}
```

---

## Testing Strategies

### 1. Mock IChatClient for Unit Tests

```csharp
public class MockChatClient : IChatClient
{
    private readonly string _mockResponse;

    public MockChatClient(string mockResponse)
    {
        _mockResponse = mockResponse;
    }

    public Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatCompletion(
            new ChatMessage(ChatRole.Assistant, _mockResponse)));
    }

    // Implement other interface members...
}

// In test:
var mockClient = new MockChatClient("Mocked summary: Added new feature");
var agent = new ChatClientAgent(mockClient, "Summarize code", "TestAgent");
var response = await agent.RunAsync("Test input");
Assert.Equal("Mocked summary: Added new feature", response.GetTextContent());
```

### 2. Integration Tests with Real LLM

```csharp
[Fact]
public async Task SummarizeAgent_RealLLM_ProducesValidOutput()
{
    var chatClient = new OpenAIClient(
        new Uri("http://localhost:5272/v1"),
        new ApiKeyCredential("test"))
        .AsChatClient("phi-4");

    var agent = new ChatClientAgent(chatClient, "Summarize code", "TestAgent");

    var response = await agent.RunAsync("Add user authentication");

    Assert.NotEmpty(response.GetTextContent());
    Assert.Contains("authentication", response.GetTextContent().ToLower());
}
```

---

## Key Takeaways

1. **`AIAgent` encapsulates common patterns** (instructions, sessions, streaming)
2. **`ChatClientAgent` wraps any `IChatClient`** for general LLM tasks
3. **`GitHubCopilotAgent` adds tool calling** for GitHub-specific operations
4. **Agent chaining enables complex workflows** (output from one → input to next)
5. **Parallel execution improves performance** for independent tasks
6. **Testing is easier with mock implementations** of `IChatClient`

---

## Practical Exercise

### Task: Add a Code Style Agent

Create a new `ChatClientAgent` that checks code style violations:

```csharp
public class CodeStyleAgent
{
    private readonly AIAgent _agent;

    public CodeStyleAgent(IChatClient chatClient)
    {
        _agent = new ChatClientAgent(
            chatClient,
            instructions: "You identify code style violations based on common best practices.",
            name: "StyleChecker");
    }

    public async Task<string> CheckStyleAsync(string diffContent)
    {
        // TODO: Use _agent.RunAsync() to analyze style
    }

    public async IAsyncEnumerable<string> CheckStyleStreamingAsync(string diffContent)
    {
        // TODO: Use _agent.RunStreamingAsync() to stream results
    }
}
```

**Steps:**
1. Implement the methods using `_agent.RunAsync()` and `_agent.RunStreamingAsync()`
2. Add this agent to `TriageWorkflow.cs` as a new step
3. Update `ConsoleUi.cs` to display style violations

---

## Next Steps

- **Module 03:** [Multi-Agent Orchestration](03-multi-agent-orchestration.md) — Coordinate multiple agents in workflows
- **Architecture Guide:** [docs/architecture.md](../architecture.md) — See the full agent interaction flow

---

## References

- [Microsoft Agent Framework - Chat Client Agent](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/chat-client-agent?pivots=programming-language-csharp)
- [Microsoft Agent Framework - GitHub Copilot Agent](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/github-copilot-agent?pivots=programming-language-csharp)
- [AIAgent API Reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.aiagent)

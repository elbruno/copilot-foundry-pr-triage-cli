# Learning Module 01: IChatClient Basics

## Introduction

[`Microsoft.Extensions.AI`](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/abstractions) provides a **provider-agnostic abstraction** for working with Large Language Models (LLMs) in .NET. At the heart of this abstraction is the `IChatClient` interface, which enables you to write code once and swap LLM providers (OpenAI, Azure OpenAI, Anthropic, local models, etc.) with configuration changes.

This module covers the fundamentals of `IChatClient` and how it's used in the Repo Triage Agent.

---

## Why IChatClient?

### The Problem: Vendor Lock-In

Without an abstraction layer, your code becomes tightly coupled to a specific LLM provider:

```csharp
// ❌ Tightly coupled to OpenAI
var openAiClient = new OpenAIClient(apiKey);
var response = await openAiClient.GetChatCompletionAsync(...);

// ❌ If you want to switch to Azure OpenAI, you must rewrite:
var azureClient = new AzureOpenAIClient(endpoint, credential);
var response = await azureClient.GetChatCompletionsAsync(...);
```

### The Solution: IChatClient Abstraction

`IChatClient` provides a **common interface** across all LLM providers:

```csharp
// ✅ Provider-agnostic code
IChatClient chatClient = GetChatClient(); // Factory method
var response = await chatClient.CompleteAsync(messages);
```

**Switching providers** requires only configuration changes, not code rewrites:

```csharp
// OpenAI
IChatClient chatClient = new OpenAIClient(apiKey)
    .AsChatClient("gpt-4");

// Azure OpenAI
IChatClient chatClient = new AzureOpenAIClient(endpoint, credential)
    .AsChatClient(deploymentName);

// Foundry Local (OpenAI-compatible)
IChatClient chatClient = new OpenAIClient(
    new Uri("http://localhost:5272/v1"),
    new ApiKeyCredential("not-needed"))
    .AsChatClient("phi-4");
```

---

## Core Concepts

### 1. Chat Messages

LLM interactions are structured as a **conversation** with alternating roles:

```csharp
var messages = new List<ChatMessage>
{
    // System message: Instructions for the AI
    new ChatMessage(ChatRole.System, 
        "You are a code review assistant. Identify security risks in code diffs."),
    
    // User message: The actual request
    new ChatMessage(ChatRole.User, 
        "Review this diff:\n\n" + diffContent),
    
    // Assistant message: Previous AI response (for multi-turn conversations)
    new ChatMessage(ChatRole.Assistant, 
        "I found 2 security risks...")
};
```

### 2. Chat Completion

The primary operation is **chat completion** — sending messages and receiving a response:

```csharp
IChatClient chatClient = GetChatClient();

var response = await chatClient.CompleteAsync(messages);

Console.WriteLine(response.Message.Text);
// Output: "I found 2 security risks: ..."
```

### 3. Streaming Responses

For better user experience, you can **stream** the response token-by-token:

```csharp
await foreach (var update in chatClient.CompleteStreamingAsync(messages))
{
    Console.Write(update.Text); // Display tokens as they arrive
}
```

---

## Using IChatClient in Repo Triage Agent

The Foundry Local Agent uses `IChatClient` to connect to the local Foundry Local endpoint:

### Implementation: FoundryAgentClient.cs

```csharp
public class FoundryAgentClient : IFoundryAgentClient
{
    private readonly IChatClient _chatClient;

    public FoundryAgentClient(string endpoint, string modelId)
    {
        // Create OpenAI-compatible client pointing to Foundry Local
        var openAiClient = new OpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential("not-needed") // Foundry Local doesn't require API key
        );

        // Convert to IChatClient with model selection
        _chatClient = openAiClient.AsChatClient(modelId);
    }

    public async Task<string> SummarizeAsync(string diffContent)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, 
                "You summarize code changes into concise bullet points."),
            new ChatMessage(ChatRole.User, 
                $"Summarize these changes:\n\n{diffContent}")
        };

        var response = await _chatClient.CompleteAsync(messages);
        return response.Message.Text;
    }

    public async Task<string> IdentifyRisksAsync(string diffContent)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, 
                "You identify security, performance, and breaking changes in code diffs."),
            new ChatMessage(ChatRole.User, 
                $"Identify risks in these changes:\n\n{diffContent}")
        };

        var response = await _chatClient.CompleteAsync(messages);
        return response.Message.Text;
    }

    // ... similar methods for GenerateChecklistAsync
}
```

### Why This Design?

1. **Flexibility** — Swap Foundry Local for Azure OpenAI by changing 3 lines of code:
   ```csharp
   var azureClient = new AzureOpenAIClient(endpoint, credential);
   _chatClient = azureClient.AsChatClient(deploymentName);
   ```

2. **Testability** — Mock `IChatClient` for unit tests without running an actual LLM

3. **Consistency** — All LLM calls follow the same pattern (messages in → response out)

---

## Advanced Features

### 1. Chat Options

Customize generation parameters:

```csharp
var options = new ChatOptions
{
    Temperature = 0.7f,        // Creativity (0.0 = deterministic, 1.0 = creative)
    MaxOutputTokens = 500,     // Limit response length
    TopP = 0.9f,               // Nucleus sampling
    FrequencyPenalty = 0.5f,   // Reduce repetition
    StopSequences = ["\n\n"]   // Stop at double newline
};

var response = await chatClient.CompleteAsync(messages, options);
```

### 2. Tool/Function Calling

Define tools that the LLM can invoke:

```csharp
var tools = new List<ChatTool>
{
    ChatTool.CreateFunctionTool(
        name: "search_code",
        description: "Search the codebase for a pattern",
        parameters: new
        {
            pattern = "The regex pattern to search"
        })
};

var options = new ChatOptions { Tools = tools };
var response = await chatClient.CompleteAsync(messages, options);

// Check if LLM wants to call a tool
if (response.Message.Contents.OfType<FunctionCallContent>().Any())
{
    var call = response.Message.Contents.OfType<FunctionCallContent>().First();
    // Execute the function and continue conversation
}
```

### 3. Embedding Generation

Generate vector embeddings for semantic search:

```csharp
IEmbeddingGenerator embeddingGen = chatClient.AsEmbeddingGenerator();
var embeddings = await embeddingGen.GenerateAsync(["Search query"]);
```

---

## Comparison: IChatClient vs. Raw HTTP

### Raw HTTP Approach (❌ Not Recommended)

```csharp
// ❌ Tightly coupled to OpenAI API format
var httpClient = new HttpClient();
var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
{
    Content = JsonContent.Create(new
    {
        model = "gpt-4",
        messages = new[]
        {
            new { role = "system", content = "..." },
            new { role = "user", content = "..." }
        }
    })
};

var response = await httpClient.SendAsync(request);
var json = await response.Content.ReadAsStringAsync();
var result = JsonSerializer.Deserialize<OpenAIResponse>(json);
```

**Problems:**
- Hard-coded API format (OpenAI-specific)
- Manual JSON serialization/deserialization
- No retry logic, no streaming, no error handling
- Cannot switch providers without complete rewrite

### IChatClient Approach (✅ Recommended)

```csharp
// ✅ Provider-agnostic, production-ready
IChatClient chatClient = GetChatClient();

var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, "..."),
    new ChatMessage(ChatRole.User, "...")
};

var response = await chatClient.CompleteAsync(messages);
```

**Benefits:**
- Works with any provider (OpenAI, Azure, Anthropic, local, etc.)
- Built-in retry logic and error handling
- Streaming support out of the box
- Type-safe message construction

---

## Practical Exercise

### Task: Add a New Analysis Step

Extend `FoundryAgentClient` with a new method that checks code style issues:

```csharp
public async Task<string> CheckCodeStyleAsync(string diffContent)
{
    var messages = new List<ChatMessage>
    {
        new ChatMessage(ChatRole.System, 
            "You are a code style analyzer. Identify style violations based on common best practices."),
        new ChatMessage(ChatRole.User, 
            $"Check code style in these changes:\n\n{diffContent}")
    };

    // TODO: Use _chatClient to get completion
    // TODO: Return the style analysis result
}
```

**Steps:**
1. Implement the method using `_chatClient.CompleteAsync()`
2. Add a new step to `TriageWorkflow.cs` to call this method
3. Update `ConsoleUi.cs` to display the style analysis

---

## Key Takeaways

1. **`IChatClient` is a provider-agnostic abstraction** for LLM interactions
2. **Conversations are structured as message lists** with roles (System, User, Assistant)
3. **Streaming improves UX** by showing tokens as they arrive
4. **Switching providers requires only configuration changes**, not code rewrites
5. **Foundry Local uses OpenAI-compatible endpoints**, so it works with `OpenAIClient`

---

## Next Steps

- **Module 02:** [AIAgent Patterns](02-aiagent-patterns.md) — Learn how `ChatClientAgent` wraps `IChatClient`
- **Module 03:** [Multi-Agent Orchestration](03-multi-agent-orchestration.md) — Coordinate multiple agents in workflows

---

## References

- [Microsoft.Extensions.AI Documentation](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/abstractions)
- [IChatClient API Reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.ichatclient)
- [Foundry Local OpenAI Compatibility](https://www.foundrylocal.ai/docs/api)

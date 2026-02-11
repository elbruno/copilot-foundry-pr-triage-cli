using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;

namespace RepoTriage.Cli.Agents;

/// <summary>
/// Foundry Local Agent implementation using Microsoft Agent Framework's ChatClientAgent.
/// Uses IChatClient abstraction pointing to Foundry Local's OpenAI-compatible endpoint.
/// See https://www.foundrylocal.ai/ for setup instructions.
///
/// EDUCATIONAL NOTE:
/// This demonstrates the ChatClientAgent pattern with IChatClient abstraction.
/// Key benefits:
///   - IChatClient abstraction enables swappable LLM backends (no vendor lock-in)
///   - Change from Foundry Local → Azure OpenAI → OpenAI with just configuration
///   - ChatClientAgent wraps IChatClient with instructions and session management
///   - Streaming via RunStreamingAsync() provides real-time token display
///
/// Configuration (env vars or user secrets):
///   FOUNDRY_LOCAL_ENDPOINT — Base URL (default: http://localhost:5273)
///   FOUNDRY_LOCAL_MODEL    — Model alias (default: phi-4)
/// </summary>
public sealed class FoundryAgentClient : IFoundryAgentClient
{
    private readonly bool _mock;
    private readonly string _endpoint;
    private readonly string _model;
    private IChatClient? _chatClient;

    /// <summary>The Foundry Local API endpoint being used.</summary>
    public string Endpoint { get; }

    /// <summary>The model name/alias being used.</summary>
    public string Model { get; }

    public FoundryAgentClient(IConfiguration config, bool mock = false, int timeoutSeconds = 300, string? modelOverride = null)
    {
        _mock = mock;
        
        // Get endpoint - default is base URL without path
        var configEndpoint = config["FOUNDRY_LOCAL_ENDPOINT"] ?? "http://localhost:5273";
        _endpoint = configEndpoint;
        _model = modelOverride ?? config["FOUNDRY_LOCAL_MODEL"] ?? "phi-4";
        
        // For display purposes
        Endpoint = configEndpoint;
        Model = _model;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken ct = default)
    {
        if (_mock)
        {
            // In mock mode, we don't need the actual chat client
            return Task.CompletedTask;
        }

        // EDUCATIONAL NOTE:
        // This demonstrates how IChatClient abstraction works:
        // 
        // 1. Create provider-specific client (OpenAI SDK in this case)
        // 2. Configure it to point to your LLM endpoint (Foundry Local here)
        // 3. Convert it to IChatClient using AsIChatClient() extension
        // 4. Now you have a provider-agnostic interface you can use anywhere
        //
        // To swap providers, just change these 3 lines:
        //   var azureClient = new AzureOpenAIClient(endpoint, credential);
        //   _chatClient = azureClient.AsChatClient(deploymentName);
        
        // Create IChatClient using OpenAI SDK for Foundry Local's OpenAI-compatible endpoint
        var endpointUri = new Uri(_endpoint);
        var baseUrl = $"{endpointUri.Scheme}://{endpointUri.Host}:{endpointUri.Port}";
        
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential("not-needed"), 
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
        
        _chatClient = openAiClient.GetChatClient(_model).AsIChatClient();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a temporary ChatClientAgent with the given system prompt.
    /// 
    /// EDUCATIONAL NOTE:
    /// We create a new agent instance for each completion type (summarize, risks, checklist)
    /// because each needs different instructions. This is more efficient than:
    ///   - Re-creating the underlying IChatClient (expensive)
    ///   - Using one agent with dynamic prompts (loses instruction benefits)
    /// ChatClientAgent is lightweight and designed for this pattern.
    /// </summary>
    private ChatClientAgent CreateTempAgent(string systemPrompt)
    {
        if (_chatClient == null)
        {
            throw new InvalidOperationException("Agent not initialized. Call InitializeAsync() first.");
        }

        return new ChatClientAgent(
            _chatClient,
            instructions: systemPrompt,
            name: "FoundryTempAgent");
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (_mock)
        {
            return GetMockResponse(systemPrompt);
        }

        // EDUCATIONAL NOTE:
        // Why use RunStreamingAsync() even for non-streaming results?
        //   - Consistent API pattern (one code path)
        //   - Can easily enable streaming by exposing the tokens
        //   - Agent handles session management automatically
        // We accumulate tokens into a StringBuilder and return the full response.
        
        // Use the agent to complete the request with streaming and accumulate the response
        var tempAgent = CreateTempAgent(systemPrompt);

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in tempAgent.RunStreamingAsync(userPrompt, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                responseBuilder.Append(update.Text);
            }
        }
        
        return responseBuilder.ToString();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string systemPrompt, 
        string userPrompt, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_mock)
        {
            // In mock mode, return the full response at once
            yield return GetMockResponse(systemPrompt);
            yield break;
        }

        // EDUCATIONAL NOTE:
        // agent.RunStreamingAsync() vs raw HttpClient streaming:
        //   ✅ Agent: Type-safe, structured responses, session management, retry logic
        //   ❌ HTTP: Manual JSON parsing, no context tracking, verbose error handling
        // The Agent Framework abstracts away the complexity while maintaining flexibility.
        
        // Use the agent for streaming completion
        var tempAgent = CreateTempAgent(systemPrompt);

        await foreach (var update in tempAgent.RunStreamingAsync(userPrompt, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    private static string GetMockResponse(string systemPrompt)
    {
        // Mock responses aligned with docs/sample.diff.patch for consistent demos
        if (systemPrompt.Contains("summarize", StringComparison.OrdinalIgnoreCase))
        {
            return """
                - Adds JWT-based authentication with login and signup endpoints
                - Implements dependency injection pattern with IUserRepository, IPasswordHasher, and ITokenService
                - Includes basic error handling for invalid credentials and duplicate emails
                - Adds unit tests for login validation scenarios
                """;
        }

        if (systemPrompt.Contains("risk", StringComparison.OrdinalIgnoreCase))
        {
            return """
                - No rate limiting on login endpoint (brute-force attack risk)
                - Password validation errors may leak user existence information
                - Token expiration policy not visible in the diff
                - No password strength requirements enforced in signup
                - Missing tests for signup edge cases and error paths
                """;
        }

        if (systemPrompt.Contains("checklist", StringComparison.OrdinalIgnoreCase))
        {
            return """
                - Verify password hashing uses a strong algorithm (bcrypt/argon2)
                - Confirm JWT tokens have a reasonable expiration time
                - Check that login errors do not leak user existence
                - Ensure signup validates email format and password strength
                - Review unit tests cover all error paths (invalid credentials, duplicate emails)
                - Verify dependency injection is properly configured
                - Check that ITokenService generates cryptographically secure tokens
                """;
        }

        return "No specific analysis available.";
    }

    public ValueTask DisposeAsync()
    {
        // IChatClient doesn't require disposal
        return ValueTask.CompletedTask;
    }
}

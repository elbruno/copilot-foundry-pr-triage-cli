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

    public FoundryAgentClient(IConfiguration config, bool mock = false)
    {
        _mock = mock;
        
        // Get endpoint - default is base URL without path
        var configEndpoint = config["FOUNDRY_LOCAL_ENDPOINT"] ?? "http://localhost:5273";
        _endpoint = configEndpoint;
        _model = config["FOUNDRY_LOCAL_MODEL"] ?? "phi-4";
        
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
        if (systemPrompt.Contains("summarize", StringComparison.OrdinalIgnoreCase))
        {
            return """
                - Adds JWT-based authentication with login and signup endpoints
                - Introduces password hashing and token generation
                - Includes basic error handling for invalid credentials
                """;
        }

        if (systemPrompt.Contains("risk", StringComparison.OrdinalIgnoreCase))
        {
            return """
                - No rate limiting on login endpoint (brute-force risk)
                - Password validation errors may leak user existence
                - Token expiration policy not visible in the diff
                """;
        }

        if (systemPrompt.Contains("checklist", StringComparison.OrdinalIgnoreCase))
        {
            return """
                - Verify password hashing uses a strong algorithm (bcrypt/argon2)
                - Confirm JWT tokens have a reasonable expiration
                - Check that login errors do not leak user existence
                - Ensure unit tests cover invalid-credential paths
                - Review token generation for secure random key usage
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

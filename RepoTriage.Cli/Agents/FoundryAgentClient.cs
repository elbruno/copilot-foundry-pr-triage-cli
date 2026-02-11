using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        // For display purposes - show the actual endpoint with /v1 that will be used
        var endpointUri = new Uri(configEndpoint);
        Endpoint = $"{endpointUri.Scheme}://{endpointUri.Host}:{endpointUri.Port}/v1";
        Model = _model;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_mock)
        {
            // In mock mode, we don't need the actual chat client
            return;
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
        // IMPORTANT: The OpenAI SDK appends /chat/completions to the endpoint, so we need
        // to include /v1 in the base URL since Foundry Local expects /v1/chat/completions
        var endpointUri = new Uri(_endpoint);
        var baseUrl = $"{endpointUri.Scheme}://{endpointUri.Host}:{endpointUri.Port}/v1";

        // Resolve model alias to full model ID
        // Foundry Local CLI uses aliases (e.g., "phi-3.5-mini") but the API expects full IDs
        var resolvedModel = await ResolveModelAliasAsync(baseUrl, _model, ct);

        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential("not-needed"),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });

        _chatClient = openAiClient.GetChatClient(resolvedModel).AsIChatClient();
    }

    /// <summary>
    /// Resolves a model alias to its full model ID by querying the /v1/models endpoint.
    /// If the alias doesn't match any known model, returns the original value.
    /// </summary>
    private static async Task<string> ResolveModelAliasAsync(string baseUrl, string modelAlias, CancellationToken ct)
    {
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetFromJsonAsync<ModelsResponse>($"{baseUrl}/models", ct);

            if (response?.Data is null)
                return modelAlias;

            // Check if the provided value matches any model ID directly (case-insensitive)
            var directMatch = response.Data.FirstOrDefault(m =>
                string.Equals(m.Id, modelAlias, StringComparison.OrdinalIgnoreCase));
            if (directMatch is not null)
                return directMatch.Id;

            // Check if it's an alias - look for models that start with the alias name
            // e.g., "phi-3.5-mini" should match "phi-3.5-mini-instruct-trtrtx-gpu:1"
            var aliasMatch = response.Data.FirstOrDefault(m =>
                m.Id.StartsWith(modelAlias, StringComparison.OrdinalIgnoreCase));
            if (aliasMatch is not null)
                return aliasMatch.Id;

            // No match found, return original value
            return modelAlias;
        }
        catch
        {
            // If we can't resolve the alias, just use what was provided
            return modelAlias;
        }
    }

    // JSON model for the /v1/models response
    private sealed class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelInfo>? Data { get; set; }
    }

    private sealed class ModelInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (_mock)
        {
            return GetMockResponse(systemPrompt);
        }

        if (_chatClient == null)
        {
            throw new InvalidOperationException("Agent not initialized. Call InitializeAsync() first.");
        }

        // EDUCATIONAL NOTE:
        // Using IChatClient directly for better compatibility with local LLM endpoints.
        // This bypasses ChatClientAgent which may add incompatible parameters.
        // The messages array follows OpenAI chat completions format.

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        };

        // Try non-streaming first for better error messages
        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? string.Empty;
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

        if (_chatClient == null)
        {
            throw new InvalidOperationException("Agent not initialized. Call InitializeAsync() first.");
        }

        // EDUCATIONAL NOTE:
        // Using IChatClient.GetStreamingResponseAsync() directly for streaming.
        // This provides real-time token delivery while maintaining compatibility.

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
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

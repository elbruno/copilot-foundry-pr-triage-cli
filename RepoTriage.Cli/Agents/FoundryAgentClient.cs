using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace RepoTriage.Cli.Agents;

/// <summary>
/// Foundry Local Agent implementation that calls a local LLM endpoint.
/// See https://www.foundrylocal.ai/ for setup instructions.
///
/// Configuration (env vars or user secrets):
///   FOUNDRY_LOCAL_ENDPOINT — Base URL (default: http://localhost:5273/v1/chat/completions)
///   FOUNDRY_LOCAL_MODEL    — Model alias (default: phi-4)
/// </summary>
public sealed class FoundryAgentClient : IFoundryAgentClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _mock;

    /// <summary>The Foundry Local API endpoint being used.</summary>
    public string Endpoint { get; }

    /// <summary>The model name/alias being used.</summary>
    public string Model { get; }

    public FoundryAgentClient(IConfiguration config, bool mock = false)
    {
        _mock = mock;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        Endpoint = config["FOUNDRY_LOCAL_ENDPOINT"]
                   ?? "http://localhost:5273/v1/chat/completions";
        Model = config["FOUNDRY_LOCAL_MODEL"]
                ?? "phi-4";
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (_mock)
        {
            return GetMockResponse(systemPrompt);
        }

        // TODO: Adjust the request body to match the actual Foundry Local API schema
        // if it differs from the OpenAI-compatible format.
        var requestBody = new
        {
            model = Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 1024,
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(Endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        // OpenAI-compatible response: choices[0].message.content
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
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

    public void Dispose() => _http.Dispose();
}

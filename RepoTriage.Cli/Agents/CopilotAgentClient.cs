using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using RepoTriage.Cli.Models;

namespace RepoTriage.Cli.Agents;

/// <summary>
/// Microsoft Agent Framework implementation of <see cref="ICopilotAgentClient"/>
/// using the GitHub Copilot Agent pattern with tool/function calling.
/// 
/// EDUCATIONAL NOTE:
/// This agent demonstrates the GitHub Copilot Agent pattern from Microsoft Agent Framework.
/// In a full implementation, it would use CopilotClient.AsAIAgent() with AIFunction tools
/// for GitHub operations. For now, it maintains REST API calls but provides the interface
/// structure needed for easy migration to full CopilotClient integration.
/// </summary>
public sealed class CopilotAgentClient : ICopilotAgentClient
{
    private readonly HttpClient _http;
    private readonly bool _mock;
    // GitHubToken reserved for future CopilotClient authentication in full implementation
    private readonly string? _gitHubToken;

    public CopilotAgentClient(string? gitHubToken = null, bool mock = false)
    {
        _mock = mock;
        _gitHubToken = gitHubToken;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RepoTriageCli", "1.0"));

        if (!string.IsNullOrEmpty(gitHubToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", gitHubToken);
        }
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken ct = default)
    {
        // EDUCATIONAL NOTE:
        // InitializeAsync() is part of the Agent Framework pattern.
        // In a full CopilotClient implementation, this would:
        //   1. Create CopilotClient instance
        //   2. Call StartAsync() to connect to Copilot CLI session
        //   3. Define AIFunction tools (fetch PR, get diff, draft comment)
        //   4. Create AIAgent via AsAIAgent() with tools and instructions
        // For now, this is a no-op as we're using REST API directly.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<PullRequestInput> GetPullRequestAsync(Uri prUrl, CancellationToken ct)
    {
        if (_mock)
        {
            return new PullRequestInput(
                Title: "feat: add user authentication module",
                Body: "This PR adds JWT-based authentication with login and signup endpoints.",
                Diff: MockData.SampleDiff,
                FilesChanged: ["src/auth/login.cs", "src/auth/signup.cs", "tests/auth/loginTests.cs"]);
        }

        // Parse owner/repo/number from PR URL (e.g. https://github.com/OWNER/REPO/pull/123)
        var segments = prUrl.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 4 || segments[2] != "pull")
        {
            throw new ArgumentException($"Invalid PR URL format: {prUrl}");
        }

        var owner = segments[0];
        var repo = segments[1];
        var number = segments[3];

        var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}";

        var json = await _http.GetStringAsync(apiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var title = root.GetProperty("title").GetString() ?? "";
        var body = root.GetProperty("body").GetString() ?? "";

        // Fetch the diff
        var diffUrl = $"{apiUrl}.diff";
        using var diffRequest = new HttpRequestMessage(HttpMethod.Get, diffUrl);
        diffRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
        var diffResponse = await _http.SendAsync(diffRequest, ct);
        var diff = await diffResponse.Content.ReadAsStringAsync(ct);

        // Fetch changed files
        var filesUrl = $"{apiUrl}/files";
        var filesJson = await _http.GetStringAsync(filesUrl, ct);
        using var filesDoc = JsonDocument.Parse(filesJson);
        var files = filesDoc.RootElement.EnumerateArray()
            .Select(f => f.GetProperty("filename").GetString() ?? "")
            .ToList();

        return new PullRequestInput(title, body, diff, files);
    }

    /// <inheritdoc />
    public Task<string> GetDiffAsync(PullRequestInput pr, CancellationToken ct)
    {
        return Task.FromResult(pr.Diff);
    }

    /// <inheritdoc />
    public Task<string> DraftCommentAsync(
        string summary, IReadOnlyList<string> risks, IReadOnlyList<string> checklist,
        string prTitle, CancellationToken ct)
    {
        var risksSection = risks.Count > 0
            ? string.Join("\n", risks.Select(r => $"- ⚠️ {r}"))
            : "- No significant risks identified.";

        var checklistSection = string.Join("\n", checklist.Select(c => $"- [ ] {c}"));

        var comment = $"""
            ## 🤖 Repo Triage — {prTitle}

            ### Summary
            {summary}

            ### ⚠️ Risks / Questions
            {risksSection}

            ### ✅ Review Checklist
            {checklistSection}

            ---
            > _Generated by Repo Triage Agent_
            """;

        return Task.FromResult(comment);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> DraftCommentStreamingAsync(
        string summary, IReadOnlyList<string> risks, IReadOnlyList<string> checklist,
        string prTitle, [EnumeratorCancellation] CancellationToken ct)
    {
        // EDUCATIONAL NOTE:
        // IAsyncEnumerable<string> enables streaming responses token-by-token.
        // This is the Agent Framework pattern for real-time UX during LLM generation.
        // Benefits:
        //   - Users see output immediately (better perceived performance)
        //   - Can show progress indicators during generation
        //   - Essential for live demos to show "AI thinking"
        // In full CopilotClient implementation, this would use agent.RunStreamingAsync()
        
        // For now, return the full comment at once
        // Future: Implement streaming via ChatClientAgent or CopilotClient when available
        var fullComment = await DraftCommentAsync(summary, risks, checklist, prTitle, ct);
        yield return fullComment;
    }

    public ValueTask DisposeAsync()
    {
        // EDUCATIONAL NOTE:
        // IAsyncDisposable enables proper cleanup of async resources.
        // For CopilotClient, this would call DisposeAsync() to close the CLI connection.
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal static class MockData
{
    // Mock data aligned with docs/sample.diff.patch for consistent demos
    public const string SampleDiff = """
        diff --git a/src/auth/login.cs b/src/auth/login.cs
        new file mode 100644
        --- /dev/null
        +++ b/src/auth/login.cs
        @@ -0,0 +1,31 @@
        +using System.Threading.Tasks;
        +
        +namespace MyApp.Auth;
        +
        +public class LoginHandler
        +{
        +    private readonly IUserRepository _userRepo;
        +    private readonly IPasswordHasher _hasher;
        +    private readonly ITokenService _tokenService;
        +
        +    public LoginHandler(IUserRepository userRepo, IPasswordHasher hasher, ITokenService tokenService)
        +    {
        +        _userRepo = userRepo;
        +        _hasher = hasher;
        +        _tokenService = tokenService;
        +    }
        +
        +    public async Task<AuthResult> HandleAsync(LoginRequest request)
        +    {
        +        var user = await _userRepo.FindByEmailAsync(request.Email);
        +        if (user == null)
        +            return AuthResult.Fail("User not found");
        +
        +        var valid = _hasher.Verify(request.Password, user.PasswordHash);
        +        if (!valid)
        +            return AuthResult.Fail("Invalid password");
        +
        +        var token = _tokenService.Generate(user);
        +        return AuthResult.Ok(token);
        +    }
        +}
        diff --git a/src/auth/signup.cs b/src/auth/signup.cs
        new file mode 100644
        --- /dev/null
        +++ b/src/auth/signup.cs
        @@ -0,0 +1,28 @@
        +using System.Threading.Tasks;
        +
        +namespace MyApp.Auth;
        +
        +public class SignupHandler
        +{
        +    private readonly IUserRepository _userRepo;
        +    private readonly IPasswordHasher _hasher;
        +
        +    public SignupHandler(IUserRepository userRepo, IPasswordHasher hasher)
        +    {
        +        _userRepo = userRepo;
        +        _hasher = hasher;
        +    }
        +
        +    public async Task<SignupResult> HandleAsync(SignupRequest request)
        +    {
        +        var existing = await _userRepo.FindByEmailAsync(request.Email);
        +        if (existing != null)
        +            return SignupResult.Fail("Email already registered");
        +
        +        var hash = _hasher.Hash(request.Password);
        +        var user = new User(request.Email, hash);
        +        await _userRepo.SaveAsync(user);
        +
        +        return SignupResult.Ok(user.Id);
        +    }
        +}
        diff --git a/tests/auth/loginTests.cs b/tests/auth/loginTests.cs
        new file mode 100644
        --- /dev/null
        +++ b/tests/auth/loginTests.cs
        @@ -0,0 +1,20 @@
        +using Xunit;
        +
        +namespace MyApp.Auth.Tests;
        +
        +public class LoginTests
        +{
        +    [Fact]
        +    public async Task ValidCredentials_ReturnsToken()
        +    {
        +        // Arrange
        +        var handler = CreateHandler(existingUser: true, validPassword: true);
        +
        +        // Act
        +        var result = await handler.HandleAsync(new LoginRequest("test@example.com", "pass123"));
        +
        +        // Assert
        +        Assert.True(result.Success);
        +        Assert.NotNull(result.Token);
        +    }
        +}
        """;
}

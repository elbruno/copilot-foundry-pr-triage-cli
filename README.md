# 🔍 Repo Triage Agent — Console TUI

A **.NET 10 (C#)** console application using **Spectre.Console** that demonstrates a **Repo Triage Agent** powered by:

- **🤖 Copilot Agent** — GitHub/PR context operations (REST-based, with Copilot SDK placeholder)
- **🧠 Foundry Local Agent** — Local LLM for summarization, risk detection, and checklist generation

The console UI clearly shows **which agent is working at each step**, making it ideal for **5–10 minute live demos**.

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Foundry Local](https://www.foundrylocal.ai/) — **required for live LLM mode** (see [Setting up Foundry Local](#setting-up-foundry-local) below)
- A **GitHub Personal Access Token** — required for `--pr` mode (can be set via environment variable or [.NET User Secrets](#environment-variables))

> **Tip:** Use the `--mock` flag to run the full demo without Foundry Local or a GitHub token. This uses deterministic sample data and skips all external calls.

### Setting up Foundry Local

The Foundry Local Agent (steps 2–4) requires [Foundry Local](https://www.foundrylocal.ai/) running on your machine. Follow these steps to install it and download the required model.

#### 1. Install the Foundry Local CLI

**Windows** (requires [winget](https://learn.microsoft.com/windows/package-manager/winget/)):

```bash
winget install Microsoft.FoundryLocal
```

**macOS** (requires [Homebrew](https://brew.sh/)):

```bash
brew tap microsoft/foundrylocal
brew install foundrylocal
```

Verify the installation:

```bash
foundry --version
```

#### 2. Download and run the model

This app uses the **phi-4** model by default. Download and start it with:

```bash
foundry model run phi-4
```

Foundry Local will download the best variant for your hardware (CUDA for NVIDIA GPUs, CPU otherwise) and start serving it. The first download may take a few minutes depending on your internet speed.

> You can browse all available models with `foundry model list` or at [foundrylocal.ai/models](https://www.foundrylocal.ai/models).
>
> To use a different model, set the `FOUNDRY_LOCAL_MODEL` environment variable (see [Environment Variables](#environment-variables)).

#### 3. Verify the endpoint

Once the model is running, Foundry Local serves an OpenAI-compatible API. To find the current endpoint:

```bash
foundry service status
```

This shows the dynamically assigned port (e.g., `http://127.0.0.1:52620/`). The chat completions endpoint is at `/v1/chat/completions`.

> **Note:** The port changes each time the service restarts. Update `FOUNDRY_LOCAL_ENDPOINT` if needed.

> **Troubleshooting:** If you see a service connection error, run `foundry service restart` and update the endpoint.

### Local Diff Mode (Recommended for Demos)

```bash
# With mock data (no LLM required)
dotnet run --project src/RepoTriage.Cli -- --diff docs/sample.diff.patch --mock

# With Foundry Local running
dotnet run --project src/RepoTriage.Cli -- --diff docs/sample.diff.patch
```

### GitHub PR Mode

```bash
# Set your GitHub token (option 1: environment variable)
export GITHUB_TOKEN=ghp_your_token_here

# Set your GitHub token (option 2: .NET user secrets — stored securely, not in source)
dotnet user-secrets set "GITHUB_TOKEN" "ghp_your_token_here" --project src/RepoTriage.Cli

# Triage a pull request
dotnet run --project src/RepoTriage.Cli -- --pr https://github.com/OWNER/REPO/pull/123

# Mock mode (no token or LLM needed)
dotnet run --project src/RepoTriage.Cli -- --pr https://github.com/OWNER/REPO/pull/123 --mock
```

---

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `FOUNDRY_LOCAL_ENDPOINT` | `http://localhost:5273/v1/chat/completions` | Foundry Local API endpoint (port changes on restart — run `foundry service status` to find it) |
| `FOUNDRY_LOCAL_MODEL` | `phi-4` | Model ID for Foundry Local (use full ID from `foundry model list`, e.g., `Phi-4-trtrtx-gpu:1`) |
| `GITHUB_TOKEN` | _(none)_ | GitHub personal access token (required for PR mode) |

> **Tip:** You can store any of these settings using [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) to avoid exposing them in your shell history or environment:
>
> ```bash
> # GitHub token
> dotnet user-secrets set "GITHUB_TOKEN" "ghp_your_token_here" --project src/RepoTriage.Cli
>
> # Foundry Local endpoint (check current port with: foundry service status)
> dotnet user-secrets set "FOUNDRY_LOCAL_ENDPOINT" "http://127.0.0.1:52620/v1/chat/completions" --project src/RepoTriage.Cli
>
> # Foundry Local model (use full model ID from: foundry model list)
> dotnet user-secrets set "FOUNDRY_LOCAL_MODEL" "Phi-4-trtrtx-gpu:1" --project src/RepoTriage.Cli
> ```
>
> The app checks both environment variables and user secrets automatically (user secrets take precedence).

---

## Command-Line Options

| Option | Description |
|---|---|
| `--diff <path>` | Path to a `.patch` / `.diff` file |
| `--pr <url>` | GitHub pull request URL |
| `--mock` | Use deterministic mock outputs (no LLM or GitHub API calls) |

---

## Workflow Steps

The triage agent runs these five steps in sequence:

| # | Agent | Step |
|---|---|---|
| 1 | 🤖 Copilot Agent | Fetch PR context / Load diff |
| 2 | 🧠 Foundry Local Agent | Summarize the change set |
| 3 | 🧠 Foundry Local Agent | Identify risks |
| 4 | 🧠 Foundry Local Agent | Generate review checklist |
| 5 | 🤖 Copilot Agent | Draft PR comment (Markdown) |

---

## Output

After completion, the tool renders four sections:

1. **📋 Summary** — Concise bullets describing the change
2. **⚠️ Risks** — Potential concerns (security, performance, breaking changes)
3. **✅ Review Checklist** — Items for reviewers to verify
4. **💬 Suggested PR Comment** — Ready-to-post Markdown comment

---

## Project Structure

```
src/
  RepoTriage.Cli/
    Program.cs                          # Minimal entry point (~80 lines)
    Workflow/
      TriageWorkflow.cs                 # Orchestration logic
      Steps.cs                          # Step definitions
    Agents/
      ICopilotAgentClient.cs            # Copilot Agent interface
      IFoundryAgentClient.cs            # Foundry Agent interface
      CopilotAgentClient.cs             # REST-based GitHub implementation
      FoundryAgentClient.cs             # Foundry Local LLM implementation
    Ui/
      ConsoleUi.cs                      # Spectre.Console rendering helpers
    Models/
      PullRequestInput.cs               # Input model
      TriageResult.cs                   # Output model
```

---

## Demo Screenshots

<!-- TODO: Add screenshots or GIF of a demo run -->

_Run `dotnet run --project src/RepoTriage.Cli -- --diff docs/sample.diff.patch --mock` to see the demo output._

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `No connection could be made because the target machine actively refused it. (localhost:5273)` | Foundry Local is not running or the model isn't loaded | Install Foundry Local and run `foundry model run phi-4` (see [Setting up Foundry Local](#setting-up-foundry-local)), or use `--mock` |
| `Request to local service failed` | Foundry Local service port binding issue | Run `foundry service restart` |
| `GitHub PR mode requires GITHUB_TOKEN` | No token configured | Set `GITHUB_TOKEN` via an environment variable or .NET user secrets (see [Environment Variables](#environment-variables)) |

---

## References

- [Foundry Local](https://www.foundrylocal.ai/) — Local LLM inference
- [GitHub REST API](https://docs.github.com/en/rest) — PR data retrieval

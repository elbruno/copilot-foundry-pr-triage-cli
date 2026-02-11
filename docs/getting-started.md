# Getting Started Guide

This guide walks you through setting up and running the Repo Triage Agent for the first time.

---

## Prerequisites

Before you begin, ensure you have the following installed:

### Required

1. **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)**
   ```bash
   # Verify installation
   dotnet --version
   # Should output: 10.0.x or higher
   ```

### Optional (for live mode)

2. **[Foundry Local](https://www.foundrylocal.ai/)** — Required for live LLM inference (not needed for `--mock` mode)

3. **[GitHub Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)** — Required for `--pr` mode with live GitHub data

---

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/elbruno/copilot-foundry-pr-triage-cli.git
cd copilot-foundry-pr-triage-cli
```

### 2. Build the Project

```bash
dotnet build RepoTriage.Cli/RepoTriage.Cli.csproj
```

**Expected output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Quick Start: Mock Mode

The fastest way to see the application in action is to use **mock mode**, which uses pre-defined sample data without requiring Foundry Local or GitHub authentication.

```bash
dotnet run --project RepoTriage.Cli -- --diff docs/sample.diff.patch --mock
```

**What happens:**
1. Loads the sample diff from `docs/sample.diff.patch`
2. Uses deterministic mock responses (no LLM calls)
3. Displays all 5 workflow steps with formatted output

**Expected runtime:** < 1 second

---

## Setup: Foundry Local (Live Mode)

To use real LLM inference, you need Foundry Local running on your machine.

### 1. Install Foundry Local CLI

**Windows** (requires [winget](https://learn.microsoft.com/windows/package-manager/winget/)):

```bash
winget install Microsoft.FoundryLocal
```

**macOS** (requires [Homebrew](https://brew.sh/)):

```bash
brew tap microsoft/foundrylocal
brew install foundrylocal
```

**Verify installation:**

```bash
foundry --version
```

### 2. Download and Run a Model

This application uses the **phi-4** model by default. Download and start it:

```bash
foundry model run phi-4
```

**What happens:**
- Foundry Local downloads the best phi-4 variant for your hardware
  - **CUDA** for NVIDIA GPUs (fastest)
  - **CPU** for systems without GPU (slower but works everywhere)
- Starts an OpenAI-compatible API endpoint
- First download takes 2-5 minutes depending on internet speed

**Output example:**
```
Downloading model: phi-4 (3.8 GB)
[████████████████████████████████] 100%
Model loaded: Phi-4-trtrtx-gpu:1
API endpoint: http://127.0.0.1:52620/v1
```

### 3. Find the Current Endpoint

Foundry Local assigns a **dynamic port** on each restart. To find the current endpoint:

```bash
foundry service status
```

**Output example:**
```
Service: Running
Endpoint: http://127.0.0.1:52620
```

### 4. Configure the Endpoint (if needed)

If the port is not the default `5273`, set the environment variable:

```bash
# Option 1: Environment variable (temporary)
export FOUNDRY_LOCAL_ENDPOINT="http://127.0.0.1:52620/v1/chat/completions"

# Option 2: .NET User Secrets (permanent, secure)
dotnet user-secrets set "FOUNDRY_LOCAL_ENDPOINT" "http://127.0.0.1:52620/v1/chat/completions" --project RepoTriage.Cli
```

---

## Run: Local Diff Mode (Live)

With Foundry Local running, analyze a local diff file:

```bash
dotnet run --project RepoTriage.Cli -- --diff docs/sample.diff.patch
```

**What happens:**
1. 🤖 **Step 1:** Loads diff from file (no GitHub API)
2. 🧠 **Step 2:** Foundry Local summarizes changes
3. 🧠 **Step 3:** Foundry Local identifies risks
4. 🧠 **Step 4:** Foundry Local generates checklist
5. 🤖 **Step 5:** Formats PR comment

**Expected runtime:** 10-20 seconds (depending on hardware)

---

## Setup: GitHub Authentication (PR Mode)

To analyze live pull requests from GitHub, you need a personal access token.

### 1. Create a GitHub Token

1. Go to [GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)](https://github.com/settings/tokens)
2. Click **"Generate new token (classic)"**
3. Set a descriptive name (e.g., "RepoTriage CLI")
4. Select scopes:
   - ✅ `repo` (required to read private repositories)
   - ✅ `public_repo` (sufficient if only reading public repositories)
5. Click **"Generate token"**
6. **Copy the token immediately** (you won't be able to see it again)

### 2. Configure the Token

**Option 1: Environment Variable (temporary, for testing)**

```bash
export GITHUB_TOKEN=ghp_your_token_here
```

**Option 2: .NET User Secrets (permanent, secure, recommended)**

```bash
dotnet user-secrets set "GITHUB_TOKEN" "ghp_your_token_here" --project RepoTriage.Cli
```

> **Why User Secrets?**  
> User Secrets stores sensitive data in your user profile directory (outside the repository), so you never accidentally commit tokens to source control.

---

## Run: GitHub PR Mode (Live)

Analyze a pull request from any public GitHub repository:

```bash
dotnet run --project RepoTriage.Cli -- --pr https://github.com/microsoft/foundry-local/pull/123
```

**What happens:**
1. 🤖 **Step 1:** Fetches PR metadata and diff from GitHub API
2. 🧠 **Step 2-4:** Foundry Local analyzes the changes
3. 🤖 **Step 5:** Formats the result

**Expected runtime:** 15-30 seconds (network + LLM inference)

---

## Run: GitHub PR Mode (Mock)

Test PR mode without a token or Foundry Local:

```bash
dotnet run --project RepoTriage.Cli -- --pr https://github.com/microsoft/foundry-local/pull/123 --mock
```

Uses sample data for all steps.

---

## Troubleshooting

### Issue: "No connection could be made to localhost:5273"

**Cause:** Foundry Local is not running or the port is incorrect.

**Fix:**
1. Start Foundry Local:
   ```bash
   foundry model run phi-4
   ```
2. Check the current port:
   ```bash
   foundry service status
   ```
3. Update the endpoint if needed:
   ```bash
   dotnet user-secrets set "FOUNDRY_LOCAL_ENDPOINT" "http://127.0.0.1:YOUR_PORT/v1/chat/completions" --project RepoTriage.Cli
   ```

**Alternative:** Use `--mock` flag to skip LLM calls.

---

### Issue: "GitHub PR mode requires GITHUB_TOKEN"

**Cause:** No GitHub token is configured.

**Fix:**
```bash
dotnet user-secrets set "GITHUB_TOKEN" "ghp_your_token_here" --project RepoTriage.Cli
```

**Alternative:** Use `--mock` flag to skip GitHub API calls.

---

### Issue: "Model inference is slow (>1 minute per step)"

**Cause:** Running on CPU instead of GPU.

**Fix:**
1. Check if you have NVIDIA GPU with CUDA support:
   ```bash
   nvidia-smi
   ```
2. If yes, download the GPU-optimized model variant:
   ```bash
   foundry model list | grep phi-4
   foundry model run Phi-4-trtrtx-gpu:1
   ```
3. If no GPU, consider using a smaller model:
   ```bash
   foundry model run phi-3.5-mini
   dotnet user-secrets set "FOUNDRY_LOCAL_MODEL" "phi-3.5-mini" --project RepoTriage.Cli
   ```

---

### Issue: Build errors about missing packages

**Cause:** NuGet packages are not restored.

**Fix:**
```bash
dotnet restore RepoTriage.Cli/RepoTriage.Cli.csproj
dotnet build RepoTriage.Cli/RepoTriage.Cli.csproj
```

---

## Command-Line Reference

```bash
dotnet run --project RepoTriage.Cli -- [OPTIONS]

Options:
  --diff <path>    Path to a .diff or .patch file
  --pr <url>       GitHub pull request URL
  --mock           Use mock data (no LLM or API calls)

Examples:
  # Local diff with live LLM
  dotnet run --project RepoTriage.Cli -- --diff docs/sample.diff.patch

  # Local diff with mock data
  dotnet run --project RepoTriage.Cli -- --diff docs/sample.diff.patch --mock

  # GitHub PR with live data
  dotnet run --project RepoTriage.Cli -- --pr https://github.com/OWNER/REPO/pull/123

  # GitHub PR with mock data
  dotnet run --project RepoTriage.Cli -- --pr https://github.com/OWNER/REPO/pull/123 --mock
```

---

## Next Steps

Once you've successfully run the application:

1. **Learn the Architecture** — Read [`docs/architecture.md`](architecture.md) to understand the agent orchestration flow
2. **Explore the Code** — Check out the learning modules in [`docs/learning/`](learning/)
3. **Customize Prompts** — Modify agent instructions in `CopilotAgentClient.cs` and `FoundryAgentClient.cs`
4. **Try Different Models** — Experiment with other Foundry Local models (see `foundry model list`)
5. **Extend Functionality** — Add new workflow steps or agent capabilities

---

## Additional Resources

- [Microsoft Agent Framework Documentation](https://learn.microsoft.com/en-us/agent-framework/)
- [Foundry Local Documentation](https://www.foundrylocal.ai/docs)
- [GitHub REST API Documentation](https://docs.github.com/en/rest)
- [Spectre.Console Documentation](https://spectreconsole.net/)

---

## Need Help?

- **Issues:** [https://github.com/elbruno/copilot-foundry-pr-triage-cli/issues](https://github.com/elbruno/copilot-foundry-pr-triage-cli/issues)
- **Discussions:** [https://github.com/elbruno/copilot-foundry-pr-triage-cli/discussions](https://github.com/elbruno/copilot-foundry-pr-triage-cli/discussions)

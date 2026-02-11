# Copilot Instructions for RepoTriage.Cli

This file contains project-specific rules and conventions for GitHub Copilot when working with this repository.

---

## Documentation Rules

### File Organization

- **All documentation** must be placed in the `docs/` folder
- **Exceptions** at repository root:
  - `README.md` - Main project documentation
  - `LICENSE` - License file
  - Code files (`.cs`, `.csproj`, `.sln`, etc.)
- **Plans and proposals** must be saved in `docs/plans/` with the naming format:
  - `plan_YYMMDD_HHMM.md` (e.g., `plan_260211_0955.md`)
  - The date/time should reflect when the plan was created

### Documentation Standards

- Use Markdown format for all documentation
- Include code examples where applicable
- Keep documentation up-to-date with code changes
- Reference Microsoft Learn documentation for Agent Framework patterns

---

## Code Conventions

### Agent Implementation

- All agents **must** implement the `AIAgent` pattern from Microsoft Agent Framework
- Use `IChatClient` abstraction for any LLM backend (enables swappable providers)
- Prefer streaming responses (`RunStreamingAsync()`) for user-facing operations
- Implement proper session management for multi-turn conversations

### Foundry Local Agent

- Use `ChatClientAgent` class from `Microsoft.Agents.AI`
- Point `IChatClient` to Foundry Local's OpenAI-compatible endpoint (`http://localhost:5272`)
- Define clear system instructions for each agent role

### GitHub Copilot Agent

- Use `CopilotClient` and `AsAIAgent()` from `Microsoft.Agents.AI.GitHub.Copilot`
- Define `AIFunction` tools for specific capabilities (PR fetching, comment drafting)
- Handle permissions appropriately for shell/file operations

### Error Handling

- Implement retry logic for LLM calls (transient failures are common)
- Provide meaningful error messages to users
- Log diagnostic information for debugging

---

## UI Conventions

- Use Spectre.Console for all terminal UI components
- Follow existing patterns in `ConsoleUi.cs`
- Support streaming token display where applicable
- Use progress indicators for long-running operations

---

## Project Structure

```
RepoTriage.Cli/
├── Agents/           # Agent implementations (IChatClient, CopilotClient patterns)
├── Models/           # Data transfer objects
├── Ui/               # Console UI components (Spectre.Console)
├── Workflow/         # Orchestration logic
└── Program.cs        # Entry point and DI configuration
```

---

## Azure Rules

- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

---

## References

- [Microsoft Agent Framework Documentation](https://learn.microsoft.com/en-us/agent-framework/)
- [GitHub Copilot Agents](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/github-copilot-agent?pivots=programming-language-csharp)
- [Chat Client Agent](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/chat-client-agent?pivots=programming-language-csharp)

using GitHub.Copilot.SDK;

namespace CopilotMemory.Extraction;

/// <summary>
/// Uses a dedicated Copilot SDK session for LLM calls (extraction, update decisions).
/// Each call creates a temporary session with the appropriate system prompt.
/// </summary>
public sealed class SdkLlmClient : ILlmClient, IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly string _model;
    private readonly bool _ownsClient;
    private bool _started;

    /// <summary>
    /// Creates a new SDK-based LLM client.
    /// </summary>
    /// <param name="model">Model name to use (default: gpt-5-mini).</param>
    /// <param name="client">Optional existing CopilotClient instance. If null, creates a new one.</param>
    public SdkLlmClient(string model = "gpt-5-mini", CopilotClient? client = null)
    {
        _ownsClient = client == null;
        _client = client ?? new CopilotClient();
        _model = model;
    }

    /// <summary>
    /// Ensures the Copilot client is started. Safe to call multiple times.
    /// </summary>
    public async Task EnsureStartedAsync()
    {
        if (!_started)
        {
            await _client.StartAsync();
            _started = true;
        }
    }

    /// <summary>
    /// Sends a prompt to the Copilot SDK and returns the assistant's response.
    /// Creates a temporary session with the system prompt and sends the user content.
    /// </summary>
    /// <param name="systemPrompt">System prompt to set context.</param>
    /// <param name="userContent">User message content.</param>
    /// <returns>The assistant's response text.</returns>
    public async Task<string> CompleteAsync(string systemPrompt, string userContent)
    {
        await EnsureStartedAsync();

        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
            SystemMessage = new SystemMessageConfig { Content = systemPrompt },
            ExcludedTools = ["bash", "edit", "write", "read_file", "glob", "grep",
                "view", "list_dir", "file_search", "create_file", "delete_file",
                "replace", "insert", "undo_edit"],
            OnPermissionRequest = PermissionHandler.ApproveAll,
        });

        var response = await session.SendAndWaitAsync(new MessageOptions
        {
            Prompt = userContent,
        });

        return response?.Data?.Content ?? "";
    }

    public async ValueTask DisposeAsync()
    {
        if (_started && _ownsClient)
        {
            await _client.StopAsync();
        }
    }
}

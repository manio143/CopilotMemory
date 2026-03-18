namespace CopilotMemory.Extraction;

/// <summary>
/// Abstraction for LLM calls. Implementations: CopilotLlm (CLI), SdkLlmClient (SDK).
/// </summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userContent);
}

/// <summary>
/// Adapter for SdkLlmClient to ILlmClient.
/// </summary>
internal sealed class SdkLlmAdapter : ILlmClient
{
    private readonly SdkLlmClient _client;

    public SdkLlmAdapter(SdkLlmClient client) => _client = client;

    public Task<string> CompleteAsync(string systemPrompt, string userContent) =>
        _client.CompleteAsync(systemPrompt, userContent);
}

/// <summary>
/// Adapter for CopilotLlm (CLI shelling) to ILlmClient.
/// </summary>
internal sealed class CliLlmAdapter : ILlmClient
{
    private readonly CopilotLlm _cli;

    public CliLlmAdapter(CopilotLlm cli) => _cli = cli;

    public async Task<string> CompleteAsync(string systemPrompt, string userContent)
    {
        return await _cli.SendAsync(systemPrompt, userContent);
    }
}

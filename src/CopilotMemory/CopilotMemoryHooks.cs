using System.Text;
using GitHub.Copilot.SDK;

namespace CopilotMemory;

/// <summary>
/// Provides Copilot SDK hooks for memory recall and capture.
/// Each hook is exposed as a separate property so consumers can pick which ones to include.
/// Usage:
///   var hooks = new CopilotMemoryHooks(pipeline);
///   var session = await client.CreateSessionAsync(new SessionConfig
///   {
///       Hooks = new SessionHooks
///       {
///           OnSessionStart = hooks.SessionStart,
///           OnUserPromptSubmitted = hooks.UserPromptSubmitted,
///       },
///       Tools = tools.All,
///   });
///   hooks.AttachCapture(session);
/// </summary>
public class CopilotMemoryHooks
{
    private readonly MemoryPipeline _pipeline;
    private readonly int _recallLimit;
    private readonly StringBuilder _assistantBuffer = new();
    private string? _lastUserPrompt;

    /// <summary>
    /// Creates a new hooks instance wrapping the given memory pipeline.
    /// </summary>
    /// <param name="pipeline">The memory pipeline to use for recall and capture.</param>
    /// <param name="recallLimit">Maximum number of memories to inject on each prompt (default: 5).</param>
    public CopilotMemoryHooks(MemoryPipeline pipeline, int recallLimit = 5)
    {
        _pipeline = pipeline;
        _recallLimit = recallLimit;
    }

    /// <summary>
    /// All hooks as a pre-built SessionHooks object. Convenience shorthand for:
    /// <code>new SessionHooks { OnSessionStart = hooks.SessionStart, OnUserPromptSubmitted = hooks.UserPromptSubmitted }</code>
    /// </summary>
    public SessionHooks All => new()
    {
        OnSessionStart = SessionStart,
        OnUserPromptSubmitted = UserPromptSubmitted,
    };

    /// <summary>
    /// Warms up the embedding model on session start (non-blocking).
    /// Assign to <c>SessionHooks.OnSessionStart</c>.
    /// </summary>
    public SessionStartHandler SessionStart =>
        async (input, invocation) =>
        {
            _ = Task.Run(() => _pipeline.WarmUp());
            return null;
        };

    /// <summary>
    /// Recalls relevant memories and injects them as additional context when the user submits a prompt.
    /// Assign to <c>SessionHooks.OnUserPromptSubmitted</c>.
    /// </summary>
    public UserPromptSubmittedHandler UserPromptSubmitted =>
        async (input, invocation) =>
        {
            _lastUserPrompt = input.Prompt;

            var recalled = _pipeline.RecallFormatted(input.Prompt, _recallLimit);

            if (string.IsNullOrWhiteSpace(recalled) || recalled.Contains("No relevant memories"))
                return null;

            return new UserPromptSubmittedHookOutput
            {
                AdditionalContext = recalled,
            };
        };

    /// <summary>
    /// Attach to a session to capture assistant messages and trigger memory extraction
    /// at the end of each assistant turn. Call this after CreateSessionAsync.
    /// </summary>
    /// <param name="session">The Copilot session to observe.</param>
    /// <returns>A disposable subscription. Dispose to stop capturing.</returns>
    public IDisposable AttachCapture(CopilotSession session)
    {
        return session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    _assistantBuffer.Append(delta.Data.DeltaContent);
                    break;

                case AssistantTurnEndEvent:
                    var userMsg = _lastUserPrompt;
                    var assistantMsg = _assistantBuffer.ToString();
                    _assistantBuffer.Clear();

                    if (!string.IsNullOrWhiteSpace(userMsg) && !string.IsNullOrWhiteSpace(assistantMsg))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _pipeline.ProcessTurnAsync(userMsg, assistantMsg);
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[CopilotMemory] Capture error: {ex.Message}");
                            }
                        });
                    }
                    break;
            }
        });
    }
}

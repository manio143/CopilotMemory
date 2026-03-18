using System.Text;
using GitHub.Copilot.SDK;

namespace CopilotMemory;

/// <summary>
/// Provides Copilot SDK hooks that wire up memory recall and capture.
/// Usage:
///   var hooks = new CopilotMemoryHooks(pipeline);
///   var session = await client.CreateSessionAsync(new SessionConfig
///   {
///       Hooks = hooks.CreateHooks(),
///   });
///   hooks.AttachCapture(session);
/// </summary>
public class CopilotMemoryHooks
{
    private readonly MemoryPipeline _pipeline;
    private readonly StringBuilder _assistantBuffer = new();
    private string? _lastUserPrompt;

    public CopilotMemoryHooks(MemoryPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>
    /// Creates a SessionHooks object to pass into SessionConfig.Hooks.
    /// </summary>
    public SessionHooks CreateHooks() => new()
    {
        OnSessionStart = OnSessionStart,
        OnUserPromptSubmitted = OnUserPromptSubmitted,
    };

    /// <summary>
    /// Attach to a session to capture assistant messages via events.
    /// Call this after CreateSessionAsync.
    /// </summary>
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

    private async Task<SessionStartHookOutput?> OnSessionStart(
        SessionStartHookInput input, HookInvocation invocation)
    {
        // Warm up async — don't block session start
        _ = Task.Run(() => _pipeline.WarmUp());
        return null;
    }

    private async Task<UserPromptSubmittedHookOutput?> OnUserPromptSubmitted(
        UserPromptSubmittedHookInput input, HookInvocation invocation)
    {
        _lastUserPrompt = input.Prompt;

        var recalled = _pipeline.RecallFormatted(input.Prompt);

        if (string.IsNullOrWhiteSpace(recalled) || recalled.Contains("No relevant memories"))
            return null;

        return new UserPromptSubmittedHookOutput
        {
            AdditionalContext = recalled,
        };
    }
}

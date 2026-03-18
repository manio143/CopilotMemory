using System.Diagnostics;
using System.Text.Json;

namespace CopilotMemory.Extraction;

/// <summary>
/// Calls Copilot CLI to run LLM prompts and parses responses from JSONL output.
/// This adapter allows using the Copilot CLI for LLM calls when the SDK is not available.
/// </summary>
public class CopilotLlm
{
    private readonly string _model;

    /// <summary>
    /// Creates a new Copilot CLI client.
    /// </summary>
    /// <param name="model">Model name to use (default: gpt-5-mini).</param>
    public CopilotLlm(string model = "gpt-5-mini")
    {
        _model = model;
    }

    /// <summary>
    /// Sends a prompt to Copilot CLI and returns the assistant's response.
    /// </summary>
    /// <param name="systemPrompt">System prompt to set context.</param>
    /// <param name="userMessage">User message to send.</param>
    /// <returns>The assistant's response text.</returns>
    public async Task<string> SendAsync(string systemPrompt, string userMessage)
    {
        // Combine system + user into a single prompt for CLI mode
        var fullPrompt = $"{systemPrompt}\n\nInput:\n{userMessage}";

        var psi = new ProcessStartInfo
        {
            FileName = "copilot",
            ArgumentList = { "-p", fullPrompt, "--model", _model, "-s", "--output-format", "json", "--no-custom-instructions" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start copilot process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Copilot exited with code {process.ExitCode}: {stderr}\nStdout: {output[..Math.Min(output.Length, 500)]}");
        }

        // Parse JSONL, find assistant.message events, concatenate content
        var content = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var type) &&
                    type.GetString() == "assistant.message" &&
                    root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("content", out var c))
                {
                    var text = c.GetString();
                    if (!string.IsNullOrEmpty(text))
                        content.Add(text);
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return string.Join("", content);
    }
}

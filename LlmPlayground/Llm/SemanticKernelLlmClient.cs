using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using RoleplaySim.Chat;

namespace RoleplaySim.Llm;

public class SemanticKernelLlmClient : ILlmClient
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ILogger<SemanticKernelLlmClient> _logger;

    private SemanticKernelLlmClient(Kernel kernel, ILogger<SemanticKernelLlmClient> logger)
    {
        _kernel = kernel;
        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public static SemanticKernelLlmClient CreateOpenAI(string apiKey, string modelId, ILogger<SemanticKernelLlmClient> logger)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId: modelId, apiKey: apiKey);
        var kernel = builder.Build();
        return new SemanticKernelLlmClient(kernel, logger);
    }

    public async Task<LlmTurnResult> CompleteAsync(List<ChatMessage> messages, LlmOptions options, CancellationToken ct = default)
    {
        var history = new ChatHistory();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "system": history.AddSystemMessage(m.Content); break;
                case "user": history.AddUserMessage(m.Content); break;
                case "assistant": history.AddAssistantMessage(m.Content); break;
                default: history.AddUserMessage(m.Content); break;
            }
        }

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            ResponseFormat = "json_object"
        };

        var result = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct).ConfigureAwait(false);
        var content = result?.Content ?? string.Empty;

        try
        {
            var parsed = JsonSerializer.Deserialize<StructuredTurn>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed is not null)
            {
                return new LlmTurnResult(
                    Thought: parsed.Thought ?? string.Empty,
                    Speak: parsed.Say ?? string.Empty,
                    Metadata: parsed.Intent is null ? null : new() { ["intent"] = parsed.Intent }
                );
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize structured LLM turn content: {Content}", content);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deserializing structured LLM turn content: {Content}", content);
            throw;
        }

        var (thought, say) = BestEffortSplit(content);
        return new LlmTurnResult(thought, say);
    }

    private (string thought, string say) BestEffortSplit(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var thought = root.TryGetProperty("thought", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var say = root.TryGetProperty("say", out var s) ? s.GetString() ?? string.Empty : content;
            return (thought, say);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse fallback LLM response content: {Content}", content);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while parsing fallback LLM response content: {Content}", content);
            throw;
        }
    }
}

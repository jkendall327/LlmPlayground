using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RoleplaySim.Chat;

namespace RoleplaySim.Llm;

public class MockLlmClient : ILlmClient
{
    public Task<LlmTurnResult> CompleteAsync(List<ChatMessage> messages, LlmOptions options, CancellationToken ct = default)
    {
        var last = messages.Last().Content;
        var say = last.Contains("?", StringComparison.Ordinal)
            ? "Let me think… here's my take."
            : "I chime in with a sharp remark.";
        var thought = "I should steer the scene toward my hidden agenda.";
        return Task.FromResult(new LlmTurnResult(thought, say));
    }
}

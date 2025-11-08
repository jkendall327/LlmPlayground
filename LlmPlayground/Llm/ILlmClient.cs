using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RoleplaySim.Chat;

namespace RoleplaySim.Llm;

public interface ILlmClient
{
    Task<LlmTurnResult> CompleteAsync(
        List<ChatMessage> messages,
        LlmOptions options,
        CancellationToken ct = default);
}

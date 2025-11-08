using System.Collections.Generic;

namespace RoleplaySim.Llm;

public record LlmTurnResult(
    string Thought,
    string Speak,
    Dictionary<string, string>? Metadata = null);

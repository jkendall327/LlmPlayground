using System.Text.Json.Serialization;

namespace RoleplaySim.Llm;

public class StructuredTurn
{
    [JsonPropertyName("thought")] public string Thought { get; set; } = string.Empty;
    [JsonPropertyName("say")] public string Say { get; set; } = string.Empty;
    [JsonPropertyName("intent")] public string? Intent { get; set; }
}

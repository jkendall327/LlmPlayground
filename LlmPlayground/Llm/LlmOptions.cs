namespace RoleplaySim.Llm;

public class LlmOptions
{
    public string Model { get; init; } = "gpt-5";
    public double Temperature { get; init; } = 1;
    public int MaxTokens { get; init; } = 2512;
}

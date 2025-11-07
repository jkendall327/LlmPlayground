// C# 10+ minimal, single-file sketch of a multi-agent roleplay system.
// Focus: personas, private thoughts, public chat, and an orchestrator loop.
// This is framework-agnostic; wire to your preferred LLM client where marked.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace RoleplaySim;

// === Basic message types ===
public enum ChannelType { Public, Private }

public record ChatEvent(
    DateTimeOffset Timestamp,
    string SenderId,
    ChannelType Channel,
    string Content,
    string[] VisibleTo // who can see this event; for Public, use all agent IDs.
);

public record ChatMessage(string Role, string Content);

// === Persona & Agent State ===
public class PersonaCard
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Unnamed";
    public string Description { get; init; } = string.Empty; // public mannerisms, speech style
    public string Backstory { get; init; } = string.Empty;    // public or semi-public
    public string Secret { get; init; } = string.Empty;       // private motives, hidden facts
}

public class AgentState
{
    public PersonaCard Persona { get; init; }
    public string AgentId => Persona.Id;
    public string DisplayName => Persona.Name;

    // Private memory known only to this agent
    public List<string> PrivateNotes { get; } = new();

    // Short-term scratchpad to carry over between turns
    public Queue<string> ShortTermThoughts { get; } = new();

    // (Optional) keys/values you might want to persist
    public Dictionary<string, string> Blackboard { get; } = new();

    public AgentState(PersonaCard persona)
    {
        Persona = persona;
        if (!string.IsNullOrWhiteSpace(persona.Secret))
            PrivateNotes.Add($"SECRET: {persona.Secret}");
    }
}

// === LLM plumbing (abstract) ===
public record LlmTurnResult(
    string Thought, // private
    string Speak,   // public
    Dictionary<string, string>? Metadata = null
);

public class LlmOptions
{
    public string Model { get; init; } = "gpt-5"; // placeholder
    public double Temperature { get; init; } = 1;
    public int MaxTokens { get; init; } = 2512;
}

public interface ILlmClient
{
    Task<LlmTurnResult> CompleteAsync(
        List<ChatMessage> messages,
        LlmOptions options,
        CancellationToken ct = default);
}

// === JSON output schema to keep public/private separate ===
public class StructuredTurn
{
    [JsonPropertyName("thought")] public string Thought { get; set; } = string.Empty; // private
    [JsonPropertyName("say")] public string Say { get; set; } = string.Empty;         // public
    [JsonPropertyName("intent")] public string? Intent { get; set; }
}

// === Agent ===
public class Agent
{
    private readonly ILlmClient _llm;
    public AgentState State { get; }

    public Agent(PersonaCard persona, ILlmClient llm)
    {
        State = new AgentState(persona);
        _llm = llm;
    }

    public async Task<(ChatEvent privateThought, ChatEvent publicSpeech)> TakeTurnAsync(
        Chatroom room,
        CancellationToken ct = default)
    {
        // Build context for this agent (public transcript window + private notes)
        var publicWindow = room.GetRecentPublicTranscript(window: room.Config.PublicWindowSize);
        var system = BuildSystemPrompt(State, room.Config);
        var userPrompt = BuildUserPrompt(publicWindow, State);

        var messages = new List<ChatMessage>
        {
            new("system", system),
            new("user", userPrompt)
        };

        var result = await _llm.CompleteAsync(messages, room.Config.LlmOptions, ct);

        // Create events (private thought is visible only to self)
        var privateEvt = new ChatEvent(
            Timestamp: DateTimeOffset.UtcNow,
            SenderId: State.AgentId,
            Channel: ChannelType.Private,
            Content: result.Thought,
            VisibleTo: new[] { State.AgentId }
        );

        var publicEvt = new ChatEvent(
            Timestamp: DateTimeOffset.UtcNow,
            SenderId: State.AgentId,
            Channel: ChannelType.Public,
            Content: result.Speak,
            VisibleTo: room.AgentIds.ToArray()
        );

        // Side-effect: stash private thought if you want persistent memory
        if (!string.IsNullOrWhiteSpace(result.Thought))
        {
            State.ShortTermThoughts.Enqueue(result.Thought);
            if (State.ShortTermThoughts.Count > room.Config.PrivateThoughtBuffer)
                State.ShortTermThoughts.Dequeue();
        }

        return (privateEvt, publicEvt);
    }

    private static string BuildSystemPrompt(AgentState state, ChatroomConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a roleplaying agent inside a multi-character chatroom.");
        sb.AppendLine("Stay in character. Write dialogue in first person, concise and lively.");
        sb.AppendLine("NEVER reveal private thoughts. Only output JSON per the schema.");
        sb.AppendLine();
        sb.AppendLine("# Persona (public)");
        sb.AppendLine(state.Persona.Description);
        if (!string.IsNullOrWhiteSpace(state.Persona.Backstory))
        {
            sb.AppendLine();
            sb.AppendLine("# Backstory (public)");
            sb.AppendLine(state.Persona.Backstory);
        }
        sb.AppendLine();
        sb.AppendLine("# Secret (private)\n" + state.Persona.Secret);
        sb.AppendLine();
        sb.AppendLine("Respond in STRICT JSON: {\"thought\": string, \"say\": string, \"intent\": string}");
        return sb.ToString();
    }

    private static string BuildUserPrompt(IEnumerable<ChatEvent> publicWindow, AgentState state)
    {
        // Provide recent public transcript and the agent's own private notes
        var transcript = string.Join("\n", publicWindow.Select(e => $"[{e.Timestamp:HH:mm}] {e.SenderId}: {e.Content}"));
        var privateNotes = string.Join("\n- ", state.PrivateNotes);

        var sb = new StringBuilder();
        sb.AppendLine("## Public Transcript (recent)");
        sb.AppendLine(transcript);
        sb.AppendLine();
        sb.AppendLine("## Your private notes (do not reveal)");
        sb.AppendLine("- " + privateNotes);
        sb.AppendLine();
        sb.AppendLine("## Task");
        sb.AppendLine("1) Think privately about goals and next move.\n2) Say one short message in character to advance the scene.");
        sb.AppendLine();
        sb.AppendLine("Return JSON only.");
        return sb.ToString();
    }
}

// === Chatroom / Orchestrator ===
public class ChatroomConfig
{
    public int PublicWindowSize { get; init; } = 20; // events
    public int MaxTurns { get; init; } = 30;
    public int PrivateThoughtBuffer { get; init; } = 8;
    public TimeSpan MinAgentDelay { get; init; } = TimeSpan.FromMilliseconds(50); // roleplay pacing
    public LlmOptions LlmOptions { get; init; } = new();
}

public class Chatroom
{
    private readonly List<Agent> _agents = new();
    private readonly List<ChatEvent> _timeline = new();
    public ChatroomConfig Config { get; }

    public Chatroom(ChatroomConfig config) => Config = config;

    public IReadOnlyList<ChatEvent> Timeline => _timeline;
    public IEnumerable<string> AgentIds => _agents.Select(a => a.State.AgentId);

    public void AddAgent(Agent agent) => _agents.Add(agent);

    public IEnumerable<ChatEvent> GetRecentPublicTranscript(int window)
        => _timeline.Where(e => e.Channel == ChannelType.Public)
                    .TakeLast(window);

    public async IAsyncEnumerable<ChatEvent> RunAsync(
        int maxTurns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var rng = new Random();
        for (int turn = 0; turn < maxTurns && !ct.IsCancellationRequested; turn++)
        {
            // Randomize speaker order each round to avoid bias
            foreach (var agent in _agents.OrderBy(_ => rng.Next()))
            {
                var (priv, pub) = await agent.TakeTurnAsync(this, ct);

                // Commit events
                _timeline.Add(priv);
                _timeline.Add(pub);

                yield return pub; // stream public events to UI

                await Task.Delay(Config.MinAgentDelay, ct);
            }
        }
    }
}

// === Example LLM client implementation ===
// Replace this with your actual OpenAI/Anthropic/etc. client that enforces JSON output.
public class MockLlmClient : ILlmClient
{
    public Task<LlmTurnResult> CompleteAsync(List<ChatMessage> messages, LlmOptions options, CancellationToken ct = default)
    {
        // Parse last user prompt just to vary output a bit.
        var last = messages.Last().Content;
        var say = last.Contains("?", StringComparison.Ordinal) ? "Let me think… here's my take." : "I chime in with a sharp remark.";
        var thought = "I should steer the scene toward my hidden agenda.";
        return Task.FromResult(new LlmTurnResult(thought, say));
    }
}

// === Bootstrapping with N personas ===

// === Semantic Kernel implementation of ILlmClient (OpenAI) ===
// NuGet: Microsoft.SemanticKernel
// using v1+ style APIs (Kernel builder + IChatCompletionService)
// Supports JSON-only responses via ResponseFormat = "json_object".

public class SemanticKernelLlmClient : ILlmClient
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;

    private SemanticKernelLlmClient(Kernel kernel)
    {
        _kernel = kernel;
        _chat = _kernel.GetRequiredService<IChatCompletionService>();
    }

    // Factory for OpenAI (non-Azure)
    public static SemanticKernelLlmClient CreateOpenAI(string apiKey, string modelId)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId: modelId, apiKey: apiKey);
        var kernel = builder.Build();
        return new SemanticKernelLlmClient(kernel);
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
            // Ask OpenAI for structured JSON
            ResponseFormat = "json_object"
        };

        var result = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
        var content = result?.Content ?? string.Empty;

        // Expected schema: {"thought": string, "say": string, "intent": string}
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
        catch
        {
            // Fall through to a best-effort split below
        }

        // Fallback: try to split private/public heuristically if JSON failed
        var (thought, say) = BestEffortSplit(content);
        return new LlmTurnResult(thought, say);
    }

    private static (string thought, string say) BestEffortSplit(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return (string.Empty, string.Empty);
        // naive: if content contains keys, try to scrape
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var thought = root.TryGetProperty("thought", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var say = root.TryGetProperty("say", out var s) ? s.GetString() ?? string.Empty : content;
            return (thought, say);
        }
        catch { /* not json */ }

        // If it's all one blob, assume it's public speech to avoid leaking thoughts
        return (string.Empty, content.Trim());
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        var key = args.First();
        var llm1 = SemanticKernelLlmClient.CreateOpenAI(key, "gpt-5");
        var llm2 = SemanticKernelLlmClient.CreateOpenAI(key, "gpt-5");
        
        var room = new Chatroom(new ChatroomConfig { MaxTurns = 5 });

        // Supply N persona cards (could be loaded from JSON/text files)
        var personas = new[]
        {
            new PersonaCard
            {
                Name = "Captain Vale",
                Description = "Gruff starship captain. Speaks in curt, decisive sentences.",
                Backstory = "Former smuggler turned reluctant hero.",
                Secret = "Owes a debt to the antagonist and may sabotage the mission."
            },
            new PersonaCard
            {
                Name = "Dr. Lumen",
                Description = "Optimistic scientist, long metaphors, curious and kind.",
                Backstory = "Left academia after a scandal to seek redemption.",
                Secret = "Carries a prototype device that could destroy the station."
            }
        };

        room.AddAgent(new(personas[0], llm1));
        room.AddAgent(new(personas[1], llm2));
        
        // Seed an opening line into the public timeline (optional)
        // You can add it by simulating a narrator/system message if desired.

        using var cts = new CancellationTokenSource();
        await foreach (var evt in room.RunAsync(room.Config.MaxTurns, cts.Token))
        {
            Console.WriteLine($"[{evt.Timestamp:HH:mm:ss}] {evt.SenderId}: {evt.Content}");
        }

        // After the run, you have both public and private events in room.Timeline.
        // Persist private events per agent if you want cross-session memory.
    }
}

/*
NOTES & EXTENSIONS
- Swap MockLlmClient with a real ILlmClient that:
  (1) Sends system+user messages to the provider
  (2) Forces JSON output (use function calling / JSON mode)
  (3) Parses to StructuredTurn and maps to LlmTurnResult

- Safety: NEVER append private thoughts to public context. Keep separate stores.
- Turn-taking: Introduce a scheduler (initiative, interrupts) or a facilitator agent.
- Tools: Add a Tool interface (e.g., Search, DiceRoll) and allow agents to request tools via the JSON "intent".
- Memory: Implement episodic (recent events), semantic (facts), and long-term (vector store) per agent.
- Visibility: For partial-visibility scenes, set VisibleTo per event to subsets of agents.
- UI: Stream IAsyncEnumerable<ChatEvent> to a console/GUI/websocket.
- Persistence: Save PersonaCard + AgentState to JSON between sessions.
*/

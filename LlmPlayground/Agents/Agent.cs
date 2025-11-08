using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RoleplaySim.Chat;
using RoleplaySim.Chatroom;
using RoleplaySim.Llm;
using ChatroomType = RoleplaySim.Chatroom.Chatroom;

namespace RoleplaySim.Agents;

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
        ChatroomType room,
        CancellationToken ct = default)
    {
        var publicWindow = room.GetRecentPublicTranscript(window: room.Config.PublicWindowSize);
        var system = BuildSystemPrompt(State, room.Config);
        var userPrompt = BuildUserPrompt(publicWindow, State);

        var messages = new List<ChatMessage>
        {
            new("system", system),
            new("user", userPrompt)
        };

        var result = await _llm.CompleteAsync(messages, room.Config.LlmOptions, ct).ConfigureAwait(false);

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

        if (!string.IsNullOrWhiteSpace(result.Thought))
        {
            State.ShortTermThoughts.Enqueue(result.Thought);
            if (State.ShortTermThoughts.Count > room.Config.PrivateThoughtBuffer)
            {
                State.ShortTermThoughts.Dequeue();
            }
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

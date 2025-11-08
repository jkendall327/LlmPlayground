using System;
using RoleplaySim.Llm;

namespace RoleplaySim.Chatroom;

public class ChatroomConfig
{
    public int PublicWindowSize { get; init; } = 20;
    public int MaxTurns { get; init; } = 30;
    public int PrivateThoughtBuffer { get; init; } = 8;
    public TimeSpan MinAgentDelay { get; init; } = TimeSpan.FromMilliseconds(50);
    public LlmOptions LlmOptions { get; init; } = new();
}

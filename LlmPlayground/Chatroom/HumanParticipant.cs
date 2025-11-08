using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RoleplaySim.Chat;

namespace RoleplaySim.Chatroom;

public class HumanParticipant : IChatParticipant
{
    private readonly string _participantId = $"user-{Guid.NewGuid():N}";

    public HumanParticipant(string displayName)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "You" : displayName.Trim();
    }

    public string ParticipantId => _participantId;

    public string DisplayName { get; }

    public Task<(ChatEvent? privateThought, ChatEvent publicSpeech)> TakeTurnAsync(
        Chatroom room,
        CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.WriteLine($"It's your turn, {DisplayName}!");

        var transcript = room.GetRecentPublicTranscript(room.Config.PublicWindowSize);
        foreach (var evt in transcript)
        {
            var name = room.GetDisplayName(evt.SenderId);
            Console.WriteLine($"[{evt.Timestamp:HH:mm}] {name}: {evt.Content}");
        }

        Console.WriteLine();

        string? input;
        do
        {
            Console.Write($"{DisplayName}: ");
            input = Console.ReadLine();

            if (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
            }
        }
        while (string.IsNullOrWhiteSpace(input));

        var timestamp = DateTimeOffset.UtcNow;
        var publicEvt = new ChatEvent(
            Timestamp: timestamp,
            SenderId: ParticipantId,
            Channel: ChannelType.Public,
            Content: input!,
            VisibleTo: room.ParticipantIds.ToArray()
        );

        return Task.FromResult<(ChatEvent?, ChatEvent)>((null, publicEvt));
    }
}

using System.Threading;
using System.Threading.Tasks;
using RoleplaySim.Chat;

namespace RoleplaySim.Chatroom;

public interface IChatParticipant
{
    string ParticipantId { get; }
    string DisplayName { get; }

    Task<(ChatEvent? privateThought, ChatEvent publicSpeech)> TakeTurnAsync(
        Chatroom room,
        CancellationToken ct = default);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleplaySim.Chat;

namespace RoleplaySim.Chatroom;

public class Chatroom
{
    private readonly List<IChatParticipant> _participants = new();
    private readonly Dictionary<string, string> _displayNames = new();
    private readonly List<ChatEvent> _timeline = new();
    private readonly ILogger<Chatroom> _logger;
    private IChatParticipant? _userParticipant;

    public Chatroom(IOptions<ChatroomConfig> options, ILogger<Chatroom> logger)
    {
        Config = options.Value;
        _logger = logger;
    }

    public ChatroomConfig Config { get; }
    public IReadOnlyList<ChatEvent> Timeline => _timeline;
    public IEnumerable<string> ParticipantIds
    {
        get
        {
            foreach (var participant in _participants)
            {
                yield return participant.ParticipantId;
            }

            if (_userParticipant is not null)
            {
                yield return _userParticipant.ParticipantId;
            }
        }
    }

    public void AddParticipant(IChatParticipant participant)
    {
        _participants.Add(participant);
        _displayNames[participant.ParticipantId] = participant.DisplayName;
        _logger.LogInformation("Participant {Participant} joined the chatroom.", participant.DisplayName);
    }

    public void SetUserParticipant(IChatParticipant participant)
    {
        _userParticipant = participant;
        _displayNames[participant.ParticipantId] = participant.DisplayName;
        _logger.LogInformation("User {Participant} joined the chatroom.", participant.DisplayName);
    }

    public string GetDisplayName(string participantId) =>
        _displayNames.TryGetValue(participantId, out var name)
            ? name
            : participantId;

    public IEnumerable<ChatEvent> GetRecentPublicTranscript(int window) =>
        _timeline.Where(e => e.Channel == ChannelType.Public)
                 .TakeLast(window);

    public async IAsyncEnumerable<ChatEvent> RunAsync(
        int maxTurns,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rng = new Random();
        for (var turn = 0; turn < maxTurns && !ct.IsCancellationRequested; turn++)
        {
            foreach (var participant in _participants.OrderBy(_ => rng.Next()))
            {
                var (priv, pub) = await participant.TakeTurnAsync(this, ct).ConfigureAwait(false);

                if (priv is not null)
                {
                    _timeline.Add(priv);
                }

                _timeline.Add(pub);

                yield return pub;

                await Task.Delay(Config.MinAgentDelay, ct).ConfigureAwait(false);
            }

            if (_userParticipant is not null)
            {
                var (priv, pub) = await _userParticipant.TakeTurnAsync(this, ct).ConfigureAwait(false);

                if (priv is not null)
                {
                    _timeline.Add(priv);
                }

                _timeline.Add(pub);

                yield return pub;
            }
        }
    }
}

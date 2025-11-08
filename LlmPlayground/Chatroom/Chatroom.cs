using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleplaySim.Agents;
using RoleplaySim.Chat;

namespace RoleplaySim.Chatroom;

public class Chatroom
{
    private readonly List<Agent> _agents = new();
    private readonly List<ChatEvent> _timeline = new();
    private readonly ILogger<Chatroom> _logger;

    public Chatroom(IOptions<ChatroomConfig> options, ILogger<Chatroom> logger)
    {
        Config = options.Value;
        _logger = logger;
    }

    public ChatroomConfig Config { get; }
    public IReadOnlyList<ChatEvent> Timeline => _timeline;
    public IEnumerable<string> AgentIds => _agents.Select(a => a.State.AgentId);

    public void AddAgent(Agent agent)
    {
        _agents.Add(agent);
        _logger.LogInformation("Agent {Agent} joined the chatroom.", agent.State.DisplayName);
    }

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
            foreach (var agent in _agents.OrderBy(_ => rng.Next()))
            {
                var (priv, pub) = await agent.TakeTurnAsync(this, ct).ConfigureAwait(false);

                _timeline.Add(priv);
                _timeline.Add(pub);

                yield return pub;

                await Task.Delay(Config.MinAgentDelay, ct).ConfigureAwait(false);
            }
        }
    }
}

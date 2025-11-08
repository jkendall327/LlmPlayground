using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleplaySim.Agents;
using RoleplaySim.Chatroom;
using ChatroomType = RoleplaySim.Chatroom.Chatroom;

namespace RoleplaySim.Simulation;

public class ChatSessionRunner
{
    private readonly ChatroomType _chatroom;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<SimulationOptions> _options;
    private readonly ILogger<ChatSessionRunner> _logger;

    public ChatSessionRunner(
        ChatroomType chatroom,
        IServiceProvider serviceProvider,
        IOptions<SimulationOptions> options,
        ILogger<ChatSessionRunner> logger)
    {
        _chatroom = chatroom;
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        foreach (var persona in _options.Value.Personas)
        {
            var agent = ActivatorUtilities.CreateInstance<Agent>(_serviceProvider, persona);
            _chatroom.AddParticipant(agent);
        }

        Console.Write("Enter your display name (leave blank for 'You'): ");
        var name = Console.ReadLine() ?? string.Empty;
        var user = new HumanParticipant(name);
        _chatroom.SetUserParticipant(user);

        await foreach (var evt in _chatroom.RunAsync(_chatroom.Config.MaxTurns, cancellationToken).ConfigureAwait(false))
        {
            var sender = _chatroom.GetDisplayName(evt.SenderId);
            _logger.LogInformation("[{Timestamp:HH:mm:ss}] {Sender}: {Content}", evt.Timestamp, sender, evt.Content);
        }
    }
}

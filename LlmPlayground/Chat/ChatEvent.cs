using System;

namespace RoleplaySim.Chat;

public record ChatEvent(
    DateTimeOffset Timestamp,
    string SenderId,
    ChannelType Channel,
    string Content,
    string[] VisibleTo);

using System;

namespace RoleplaySim.Agents;

public class PersonaCard
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Unnamed";
    public string Description { get; init; } = string.Empty;
    public string Backstory { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
}

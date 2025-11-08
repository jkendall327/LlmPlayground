using System;
using System.Collections.Generic;

namespace RoleplaySim.Agents;

public class AgentState
{
    public PersonaCard Persona { get; }
    public string AgentId => Persona.Id;
    public string DisplayName => Persona.Name;

    public List<string> PrivateNotes { get; } = new();
    public Queue<string> ShortTermThoughts { get; } = new();
    public Dictionary<string, string> Blackboard { get; } = new();

    public AgentState(PersonaCard persona)
    {
        Persona = persona;
        if (!string.IsNullOrWhiteSpace(persona.Secret))
        {
            PrivateNotes.Add($"SECRET: {persona.Secret}");
        }
    }
}

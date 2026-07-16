namespace CesiumAI.Api.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public required Uri Endpoint { get; init; }

    public required string ApiKey { get; init; }

    public required string Model { get; init; }
}

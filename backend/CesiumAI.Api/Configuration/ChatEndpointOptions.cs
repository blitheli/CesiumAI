namespace CesiumAI.Api.Configuration;

public sealed class ChatEndpointOptions
{
    public const string SectionName = "ChatEndpoint";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}

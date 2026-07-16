namespace CesiumAI.Api.Configuration;

public sealed class AstroxOptions
{
    public const string SectionName = "Astrox";

    public required Uri BaseUrl { get; init; }

    public int DefaultStepSeconds { get; init; } = 60;

    public double DefaultDescendingNodeLocalTime { get; init; } = 10.5;
}

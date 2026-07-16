using CesiumAI.Api.Configuration;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Configuration;

public sealed class ChatEndpointOptionsTests
{
    [Fact]
    public void Timeout_DefaultsToExactly120Seconds()
    {
        var options = new ChatEndpointOptions();

        options.Timeout.Should().Be(TimeSpan.FromSeconds(120));
    }
}

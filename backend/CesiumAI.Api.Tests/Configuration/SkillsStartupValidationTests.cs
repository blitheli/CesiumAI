using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Configuration;

public sealed class SkillsStartupValidationTests
{
    [Fact]
    public void MissingSkillsDirectory_FailsApplicationStartup_EvenWhenChatServiceIsReplaced()
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cesiumai-missing-skills-{Guid.NewGuid():N}");
        using var factory = new ApiFactory(Environments.Development, missingDirectory);

        Action act = () =>
        {
            using HttpClient _ = factory.CreateClient();
        };

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Skills directory*does not exist*");
    }

    [Fact]
    public async Task ExistingFixtureSkillsDirectory_AllowsApplicationStartup()
    {
        string skillsDirectory =
            Directory.CreateTempSubdirectory("cesiumai-valid-skills-").FullName;

        try
        {
            using var factory = new ApiFactory(
                Environments.Development,
                skillsDirectory);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                "/weatherforecast");

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally
        {
            Directory.Delete(skillsDirectory, recursive: true);
        }
    }
}

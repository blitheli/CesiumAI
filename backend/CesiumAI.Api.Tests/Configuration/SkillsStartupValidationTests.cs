using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Configuration;

public sealed class SkillsStartupValidationTests
{
    [Fact]
    public void MissingSkillsDirectory_FailsApplicationStartup_EvenWhenChatServiceIsReplaced()
    {
        // 与 content root 同盘，避免 GetRelativePath 跨盘变成绝对路径后误报 “must be relative”
        string missingDirectory = Path.Combine(
            AppContext.BaseDirectory,
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
        string skillsDirectory = ApiFactory.CreateOwnedSkillsDirectory();

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

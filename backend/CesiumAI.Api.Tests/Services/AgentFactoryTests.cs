using System.Net;
using System.Text.Json;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Services;
using CesiumAI.Api.Tests.TestSupport;
using CesiumAI.Api.Tools;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Services;

public class AgentFactoryTests
{
    [Fact]
    public void SkillsProviderOptions_DisableApprovalOnlyForReadOnlySkillTools()
    {
        AgentSkillsProviderOptions options = AgentFactory.CreateSkillsProviderOptions();

        options.DisableLoadSkillApproval.Should().BeTrue();
        options.DisableReadSkillResourceApproval.Should().BeTrue();
        options.DisableRunSkillScriptApproval.Should().BeFalse();
    }

    [Fact]
    public void Instructions_ContainEveryRequiredSafetyPolicy()
    {
        AgentInstructions.Text.Should().Contain("场景变更").And.Contain("场景工具");
        AgentInstructions.Text.Should().Contain("可执行 CZML").And.Contain("助手文本");
        AgentInstructions.Text.Should().Contain("纯问题").And.Contain("不调用场景工具");
        AgentInstructions.Text.Should().Contain("AddSatelliteJ2").And.Contain("唯一");
        AgentInstructions.Text.Should().Contain("简洁中文");
    }

    [Fact]
    public void Constructor_ThrowsClearError_WhenResolvedSkillsDirectoryDoesNotExist()
    {
        string root = Directory.CreateTempSubdirectory().FullName;

        try
        {
            Action act = () => CreateFactory(root, "../missing-skills");

            DirectoryNotFoundException exception =
                act.Should().Throw<DirectoryNotFoundException>().Which;
            string expectedPath = Path.GetFullPath(Path.Combine(root, "../missing-skills"));
            exception.Message.Should().Be(
                $"Skills directory '{expectedPath}' does not exist. Configure Skills:Path as a path relative to the application content root.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Constructor_RejectsAbsoluteSkillsPath()
    {
        string contentRoot = Directory.CreateTempSubdirectory().FullName;

        try
        {
            Action act = () => CreateFactory(contentRoot, contentRoot);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*Skills:Path*relative*content root*");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ResolvesSkillsRelativeToContentRoot_WithoutCallingRemoteServices()
    {
        string parent = Directory.CreateTempSubdirectory().FullName;
        string contentRoot = Directory.CreateDirectory(Path.Combine(parent, "api")).FullName;
        Directory.CreateDirectory(Path.Combine(parent, "astrox-skills", "skills"));

        try
        {
            AgentFactory factory = CreateFactory(contentRoot, "../astrox-skills/skills");

            AgentRuntime runtime = await factory.CreateAsync("session", CancellationToken.None);

            runtime.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static AgentFactory CreateFactory(string contentRoot, string skillsPath)
    {
        var rawClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))));
        var astroxOptions = Options.Create(new AstroxOptions
        {
            BaseUrl = new Uri("https://astrox.example/")
        });
        var rawTools = new AstroxRawTools(rawClient, astroxOptions);

        return new AgentFactory(
            Options.Create(new AgentOptions
            {
                Endpoint = new Uri("https://llm.example/v1"),
                ApiKey = "test-key",
                Model = "test-model"
            }),
            Options.Create(new SkillsOptions { Path = skillsPath }),
            new StubHostEnvironment(contentRoot),
            new StubOrbitScenarioService(),
            rawTools,
            NullLoggerFactory.Instance);
    }

    private sealed class StubOrbitScenarioService : IOrbitScenarioService
    {
        public Task<JsonElement> CreateSsoJ2PacketAsync(
            SsoJ2Scenario scenario,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Agent creation must not call Astrox.");

        public Task<JsonElement> CreatePacketFromPropagationAsync(
            string id,
            string name,
            string propagatorPath,
            JsonElement request,
            DateTimeOffset startUtc,
            DateTimeOffset stopUtc,
            string? orbitHint,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Agent creation must not call Astrox.");

        public JsonElement CreatePacketFromPositions(
            string id,
            string name,
            JsonElement position,
            DateTimeOffset startUtc,
            DateTimeOffset stopUtc,
            string? orbitHint) =>
            throw new InvalidOperationException("Agent creation must not call Astrox.");
    }

    private sealed class StubHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CesiumAI.Api.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

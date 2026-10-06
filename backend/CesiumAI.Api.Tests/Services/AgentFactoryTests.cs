using System.Net;
using System.Reflection;
using System.Text.Json;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using CesiumAI.Api.Tests.TestSupport;
using CesiumAI.Api.Tools;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Services;

public class AgentFactoryTests
{
    private static readonly string[] RequiredCameraAndStyleTools =
    [
        "FocusEntity",
        "TrackEntity",
        "StopTracking",
        "AdjustCamera",
        "OrbitEntity",
        "StopOrbit",
        "UpdateEntityStyle"
    ];

    private static readonly string[] RequiredGenericOrbitTools =
    [
        "PropagateAndAddSatellite",
        "AddSatelliteFromPositions",
        "PropagateIssAndAddSatellite"
    ];

    [Fact]
    public void SkillsProviderOptions_DisableApprovalOnlyForReadOnlySkillTools()
    {
        AgentSkillsProviderOptions options = AgentFactory.CreateSkillsProviderOptions();

        options.DisableLoadSkillApproval.Should().BeTrue();
        options.DisableReadSkillResourceApproval.Should().BeTrue();
        options.DisableRunSkillScriptApproval.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_RegistersCameraStyleAndGenericOrbitTools()
    {
        string parent = Directory.CreateTempSubdirectory().FullName;
        string contentRoot = Directory.CreateDirectory(Path.Combine(parent, "api")).FullName;
        Directory.CreateDirectory(Path.Combine(contentRoot, "skills"));

        try
        {
            AgentFactory factory = CreateFactory(contentRoot, "skills");
            AgentRuntime runtime = await factory.CreateAsync("session", CancellationToken.None);

            runtime.Agent.Should().BeOfType<HarnessAgent>();
            ChatClientAgent agent = runtime.Agent!.GetService<ChatClientAgent>()!;
            IList<AITool>? tools = ReadChatOptions(agent)?.Tools;
            tools.Should().NotBeNull();

            string[] names = tools!.Select(tool => tool.Name).ToArray();
            names.Should().Contain(RequiredCameraAndStyleTools);
            names.Should().Contain(RequiredGenericOrbitTools);
            names.Should().Contain(
            [
                "ClearScene",
                "UpsertFacility",
                "DeleteEntity",
                "AddSatelliteJ2",
                "HttpGet",
                "HttpPost"
            ]);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_BuildsHarnessAgentWithOnlyAppInstructionsToolsAndSkills()
    {
        string parent = Directory.CreateTempSubdirectory().FullName;
        string contentRoot = Directory.CreateDirectory(Path.Combine(parent, "api")).FullName;
        Directory.CreateDirectory(Path.Combine(contentRoot, "skills"));

        try
        {
            AgentFactory factory = CreateFactory(contentRoot, "skills");
            AgentRuntime runtime = await factory.CreateAsync("session", CancellationToken.None);

            ChatClientAgent agent = runtime.Agent!.GetService<ChatClientAgent>()!;
            agent.Name.Should().Be("SpaceAgent");
            agent.Instructions.Should().Be(AgentInstructions.Text);
            ReadChatOptions(agent)!.Tools.Should().NotContain(tool => tool is HostedWebSearchTool);
            agent.AIContextProviders.Should().ContainSingle()
                .Which.Should().BeOfType<AgentSkillsProvider>();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task RunStreamingAsync_ThroughHarness_InvokesSceneToolAndStreamsText()
    {
        string parent = Directory.CreateTempSubdirectory().FullName;
        string contentRoot = Directory.CreateDirectory(Path.Combine(parent, "api")).FullName;
        Directory.CreateDirectory(Path.Combine(contentRoot, "skills"));
        var chatClient = new ScriptedToolCallingChatClient("ClearScene", ["已清空", "场景。"]);

        try
        {
            AgentFactory factory = CreateFactory(contentRoot, "skills", () => chatClient);
            var store = new AgentRuntimeStore(factory);
            var collector = new SceneOpCollector();

            var updates = new List<AgentResponseUpdate>();
            await foreach (AgentResponseUpdate update in store.RunStreamingAsync(
                "session",
                "清空当前场景",
                collector,
                CancellationToken.None))
            {
                updates.Add(update);
            }

            List<AIContent> contents = updates.SelectMany(update => update.Contents).ToList();
            contents.OfType<FunctionCallContent>().Should().ContainSingle()
                .Which.Name.Should().Be("ClearScene");
            contents.OfType<FunctionResultContent>().Should().ContainSingle()
                .Which.Exception.Should().BeNull();
            string.Concat(updates.Select(update => update.Text)).Should().Be("已清空场景。");
            collector.Drain().Should().ContainSingle().Which.Should().BeOfType<ClearSceneOp>();
            chatClient.CallCount.Should().Be(2);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
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
        Directory.CreateDirectory(Path.Combine(contentRoot, "skills"));

        try
        {
            AgentFactory factory = CreateFactory(contentRoot, "skills");

            AgentRuntime runtime = await factory.CreateAsync("session", CancellationToken.None);

            runtime.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    // ChatClientAgent.ChatOptions 为非公开属性，测试通过反射只读检查注册结果。
    private static ChatOptions? ReadChatOptions(ChatClientAgent agent) =>
        typeof(ChatClientAgent)
            .GetProperty("ChatOptions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(agent) as ChatOptions;

    private static AgentFactory CreateFactory(
        string contentRoot,
        string skillsPath,
        Func<IChatClient>? chatClientFactory = null)
    {
        var rawClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))));
        var astroxOptions = Options.Create(new AstroxOptions
        {
            BaseUrl = new Uri("https://astrox.example/")
        });
        var rawTools = new AstroxRawTools(rawClient, astroxOptions);

        if (chatClientFactory is not null)
        {
            return new AgentFactory(
                chatClientFactory,
                Options.Create(new SkillsOptions { Path = skillsPath }),
                new StubHostEnvironment(contentRoot),
                new StubOrbitScenarioService(),
                new SceneStyleValidator(),
                rawTools,
                NullLoggerFactory.Instance);
        }

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
            new SceneStyleValidator(),
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

    /// <summary>
    /// 第一次调用流式返回一个工具调用；工具结果回传后的第二次调用流式返回文本分片。
    /// </summary>
    private sealed class ScriptedToolCallingChatClient(string toolName, string[] textChunks) : IChatClient
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试只覆盖流式路径。");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref _callCount);
            await Task.Yield();

            if (call == 1)
            {
                options!.Tools.Should().Contain(tool => tool.Name == toolName);
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", toolName, new Dictionary<string, object?>())]);
                yield break;
            }

            messages.SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Should().Contain(result => result.CallId == "call-1");
            foreach (string chunk in textChunks)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class StubHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CesiumAI.Api.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

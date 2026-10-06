using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ClientModel;

namespace CesiumAI.Api.Services;

public sealed class AgentFactory : IAgentRuntimeFactory, IDisposable
{
    private readonly Func<IChatClient> _chatClientFactory;
    private readonly IOrbitScenarioService _orbitScenarioService;
    private readonly ISceneStyleValidator _styleValidator;
    private readonly AstroxRawTools _rawTools;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AgentSkillsProvider _skillsProvider;

    public AgentFactory(
        IOptions<AgentOptions> agentOptions,
        IOptions<SkillsOptions> skillsOptions,
        IHostEnvironment hostEnvironment,
        IOrbitScenarioService orbitScenarioService,
        ISceneStyleValidator styleValidator,
        AstroxRawTools rawTools,
        ILoggerFactory loggerFactory)
        : this(
            CreateOpenAIChatClientFactory(
                (agentOptions ?? throw new ArgumentNullException(nameof(agentOptions))).Value),
            skillsOptions,
            hostEnvironment,
            orbitScenarioService,
            styleValidator,
            rawTools,
            loggerFactory)
    {
    }

    /// <summary>
    /// 测试入口：注入任意 <see cref="IChatClient"/>，以便不访问外部 LLM 也能驱动完整 Harness 管线。
    /// </summary>
    internal AgentFactory(
        Func<IChatClient> chatClientFactory,
        IOptions<SkillsOptions> skillsOptions,
        IHostEnvironment hostEnvironment,
        IOrbitScenarioService orbitScenarioService,
        ISceneStyleValidator styleValidator,
        AstroxRawTools rawTools,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(skillsOptions);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        _chatClientFactory = chatClientFactory
            ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _orbitScenarioService = orbitScenarioService
            ?? throw new ArgumentNullException(nameof(orbitScenarioService));
        _styleValidator = styleValidator ?? throw new ArgumentNullException(nameof(styleValidator));
        _rawTools = rawTools ?? throw new ArgumentNullException(nameof(rawTools));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        string skillsPath = skillsOptions.Value.ResolveExistingDirectory(
            hostEnvironment.ContentRootPath);
        _skillsProvider = new AgentSkillsProvider(
            skillsPath,
            options: CreateSkillsProviderOptions(),
            loggerFactory: _loggerFactory);
    }

    public async Task<AgentRuntime> CreateAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id cannot be blank.", nameof(sessionId));
        }

        var sceneOpSink = new TurnSceneOpSink();
        var sceneTools = new SceneTools(
            sceneOpSink,
            _orbitScenarioService,
            styleValidator: _styleValidator);
        List<AITool> tools =
        [
            AIFunctionFactory.Create(sceneTools.ClearScene),
            AIFunctionFactory.Create(sceneTools.UpsertFacility),
            AIFunctionFactory.Create(sceneTools.DeleteEntity),
            AIFunctionFactory.Create(sceneTools.AddSatelliteJ2),
            AIFunctionFactory.Create(sceneTools.FocusEntity),
            AIFunctionFactory.Create(sceneTools.TrackEntity),
            AIFunctionFactory.Create(sceneTools.StopTracking),
            AIFunctionFactory.Create(sceneTools.AdjustCamera),
            AIFunctionFactory.Create(sceneTools.OrbitEntity),
            AIFunctionFactory.Create(sceneTools.StopOrbit),
            AIFunctionFactory.Create(sceneTools.UpdateEntityStyle),
            AIFunctionFactory.Create(sceneTools.PropagateAndAddSatellite),
            AIFunctionFactory.Create(sceneTools.AddSatelliteFromPositions),
            AIFunctionFactory.Create(sceneTools.PropagateIssAndAddSatellite),
            AIFunctionFactory.Create(_rawTools.HttpGet),
            AIFunctionFactory.Create(_rawTools.HttpPost)
        ];

        AIAgent agent = _chatClientFactory().AsHarnessAgent(
            CreateHarnessOptions(tools, _skillsProvider),
            loggerFactory: _loggerFactory);

        AgentSession session = await agent.CreateSessionAsync(cancellationToken);
        return new AgentRuntime(agent, session, sceneOpSink);
    }

    public void Dispose() => _skillsProvider.Dispose();

    private static Func<IChatClient> CreateOpenAIChatClientFactory(AgentOptions agentOptions) =>
        () => new OpenAIClient(
                new ApiKeyCredential(agentOptions.ApiKey),
                new OpenAIClientOptions { Endpoint = agentOptions.Endpoint })
            .GetChatClient(agentOptions.Model)
            .AsIChatClient();

    /// <summary>
    /// Harness 默认开启的若干能力与本应用不匹配，需显式关闭：
    /// <list type="bullet">
    /// <item>WebSearch：<see cref="HostedWebSearchTool"/> 需要服务端托管搜索，OpenAI 兼容的 Chat Completions（如 Moonshot）不支持。</item>
    /// <item>FileMemory：默认写入进程工作目录 agent-file-memory/，IIS 部署下不可控且场景助手无需跨轮文件记忆。</item>
    /// <item>Todo / AgentMode：面向长任务规划（plan/execute），会额外注入工具与指令，干扰“场景变更只走场景工具”的约束。</item>
    /// <item>内置 AgentSkillsProvider：默认按进程工作目录发现 skills；改为传入按 content root 解析、带审批配置的同一实例。</item>
    /// <item>HarnessInstructions 置空：默认英文通用指令要求在工具调用间解释推理，与 <see cref="AgentInstructions"/> 的“简洁中文”冲突。</item>
    /// </list>
    /// 保留默认的函数调用循环、逐次服务调用历史持久化、工具审批与 OpenTelemetry 装饰器。
    /// 循环内压缩（MaxContextWindowTokens / MaxOutputTokens）在 1.23.0 仍为实验性 API（MAAI001），暂不启用。
    /// </summary>
    internal static HarnessAgentOptions CreateHarnessOptions(
        IList<AITool> tools,
        AgentSkillsProvider skillsProvider) =>
        new()
        {
            Name = "SpaceAgent",
            Description = "航天任务设计与 Cesium 场景助手",
            HarnessInstructions = string.Empty,
            ChatOptions = new ChatOptions
            {
                Instructions = AgentInstructions.Text,
                Tools = tools
            },
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            AIContextProviders = [skillsProvider]
        };

    internal static AgentSkillsProviderOptions CreateSkillsProviderOptions() =>
        new()
        {
            DisableLoadSkillApproval = true,
            DisableReadSkillResourceApproval = true,
            DisableRunSkillScriptApproval = false
        };
}

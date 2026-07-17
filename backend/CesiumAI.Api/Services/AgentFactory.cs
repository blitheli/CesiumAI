using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace CesiumAI.Api.Services;

public sealed class AgentFactory : IAgentRuntimeFactory, IDisposable
{
    private readonly AgentOptions _agentOptions;
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
    {
        ArgumentNullException.ThrowIfNull(agentOptions);
        ArgumentNullException.ThrowIfNull(skillsOptions);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        _agentOptions = agentOptions.Value;
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

        var client = new OpenAIClient(
            new ApiKeyCredential(_agentOptions.ApiKey),
            new OpenAIClientOptions { Endpoint = _agentOptions.Endpoint });

        AIAgent agent = client.GetChatClient(_agentOptions.Model).AsAIAgent(
            new ChatClientAgentOptions
            {
                Name = "SpaceAgent",
                Description = "航天任务设计与 Cesium 场景助手",
                ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions.Text,
                    Tools = tools
                },
                AIContextProviders = [_skillsProvider]
            },
            loggerFactory: _loggerFactory);

        AgentSession session = await agent.CreateSessionAsync(cancellationToken);
        return new AgentRuntime(agent, session, sceneOpSink);
    }

    public void Dispose() => _skillsProvider.Dispose();

    internal static AgentSkillsProviderOptions CreateSkillsProviderOptions() =>
        new()
        {
            DisableLoadSkillApproval = true,
            DisableReadSkillResourceApproval = true,
            DisableRunSkillScriptApproval = false
        };
}

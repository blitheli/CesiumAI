using System.Net;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Services;
using CesiumAI.Api.Tools;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

// =============================================================================
// CesiumAI.Api 入口：最小托管模型（top-level statements）
//
// 职责概览：
// 1. 绑定并校验配置（Agent / Astrox / Skills / Chat / 反向代理）
// 2. 注册 HTTP 客户端与业务服务（对话、轨道场景、Agent 运行时等）
// 3. 配置中间件管道（转发头、HTTPS、开发 CORS）
// 4. 映射控制器与健康检查端点，然后启动主机
//
// 启动时 ValidateOnStart：若 ApiKey 为空、Endpoint/BaseUrl 非法，或 skills 目录不存在，
// 会在启动阶段立即失败，而不是等到第一次请求才暴露问题。
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// 开发环境 CORS 策略名：仅允许 Vite 前端（localhost:5173）跨域调用 API
const string DevelopmentCorsPolicy = "DevelopmentFrontend";

// 具名 HttpClient：用于 Astrox 原始 HTTP 工具（不走 AstroxClient 封装，直接转发/探测）
const string AstroxRawClient = "AstroxRaw";

// ---------------------------------------------------------------------------
// 基础框架服务
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// ---------------------------------------------------------------------------
// 配置绑定与校验（Options 模式）
// 全部使用 ValidateOnStart：配置错误在 app.Build() / 启动时即失败，避免“半启动”状态。
// ---------------------------------------------------------------------------

// 聊天端点超时等运行时参数（如单次对话最长等待时间）
builder.Services
    .AddOptions<ChatEndpointOptions>()
    .Bind(builder.Configuration.GetSection(ChatEndpointOptions.SectionName))
    .Validate(
        options => options.Timeout > TimeSpan.Zero,
        "ChatEndpoint:Timeout must be greater than zero.")
    .ValidateOnStart();

// LLM Agent 配置：OpenAI 兼容 Endpoint、ApiKey、Model
// 无有效 ApiKey 时服务可启动到 /healthz，但真实 POST /api/chat 会因鉴权失败返回 500
builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(
        options => options.Endpoint is { IsAbsoluteUri: true }
            && (options.Endpoint.Scheme == Uri.UriSchemeHttp
                || options.Endpoint.Scheme == Uri.UriSchemeHttps),
        "Agent:Endpoint must be an absolute HTTP or HTTPS URL.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey),
        "Agent:ApiKey is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Model),
        "Agent:Model is required.")
    .ValidateOnStart();

// Astrox 轨道/场景后端：BaseUrl 与若干默认仿真参数
builder.Services
    .AddOptions<AstroxOptions>()
    .Bind(builder.Configuration.GetSection(AstroxOptions.SectionName))
    .Validate(
        options => options.BaseUrl is { IsAbsoluteUri: true }
            && (options.BaseUrl.Scheme == Uri.UriSchemeHttp
                || options.BaseUrl.Scheme == Uri.UriSchemeHttps),
        "Astrox:BaseUrl must be an absolute HTTP or HTTPS URL.")
    .Validate(
        options => options.DefaultStepSeconds is >= 1 and <= 3600,
        "Astrox:DefaultStepSeconds must be between 1 and 3600.")
    .Validate(
        options => options.DefaultDescendingNodeLocalTime is >= 0 and < 24,
        "Astrox:DefaultDescendingNodeLocalTime must be between 0 inclusive and 24 exclusive.")
    .ValidateOnStart();

// Skills 目录配置；路径存在性等由 SkillsOptionsValidator 在启动时校验
// （submodule astrox-skills 需同步到 API content root 下的 skills/）
builder.Services
    .AddOptions<SkillsOptions>()
    .Bind(builder.Configuration.GetSection(SkillsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SkillsOptions>, SkillsOptionsValidator>();

// 反向代理信任列表：仅这些 IP 的 X-Forwarded-* 头会被采纳（防伪造协议/主机）
builder.Services
    .AddOptions<ReverseProxyOptions>()
    .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
    .Validate(
        options => options.KnownProxies is { Length: > 0 }
            && options.KnownProxies.All(
                proxy => IPAddress.TryParse(proxy, out _)),
        "ReverseProxy:KnownProxies must contain only trusted proxy IP addresses.")
    .ValidateOnStart();

// 将 ReverseProxy:KnownProxies 注入 ForwardedHeadersOptions：
// 只转发 X-Forwarded-Proto（正确识别 HTTPS），且 ForwardLimit=1，避免多层伪造
builder.Services
    .AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<ReverseProxyOptions>>((options, reverseProxyOptions) =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        // 清空框架默认的“信任本地回环”等网络，改为仅信任显式配置的代理 IP
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (string proxy in reverseProxyOptions.Value.KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    });

// ---------------------------------------------------------------------------
// HttpClient：对接 Astrox
// AllowAutoRedirect = false：避免跟随重定向导致意外改写请求或泄露凭证
// ---------------------------------------------------------------------------

// 类型化客户端：业务代码通过 IAstroxClient 调用 Astrox API
builder.Services
    .AddHttpClient<IAstroxClient, AstroxClient>((services, client) =>
    {
        client.BaseAddress = services.GetRequiredService<IOptions<AstroxOptions>>().Value.BaseUrl;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    });

// 具名客户端：供 AstroxRawTools 做原始 HTTP 调用（与封装客户端分离，便于独立配置/观测）
builder.Services
    .AddHttpClient(AstroxRawClient, (services, client) =>
    {
        client.BaseAddress = services.GetRequiredService<IOptions<AstroxOptions>>().Value.BaseUrl;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    });

// ---------------------------------------------------------------------------
// 业务服务注册
// ---------------------------------------------------------------------------

// 可测试的时钟抽象；生产使用系统时钟
builder.Services.AddSingleton(TimeProvider.System);

// CZML 位置与场景样式校验器（Agent 产出场景数据前的结构/语义检查）
builder.Services.AddSingleton<ICzmlPositionValidator, CzmlPositionValidator>();
builder.Services.AddSingleton<ISceneStyleValidator, SceneStyleValidator>();

// 轨道场景编排（构建/转换场景相关数据）
builder.Services.AddSingleton<IOrbitScenarioService, OrbitScenarioService>();

// Astrox 原始工具：内部持有具名 HttpClient + AstroxOptions
builder.Services.AddSingleton<AstroxRawTools>(services =>
    new AstroxRawTools(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(AstroxRawClient),
        services.GetRequiredService<IOptions<AstroxOptions>>()));

// Agent 工厂与按会话/轮次运行的运行时存储（创建与复用 Agent 实例）
builder.Services.AddSingleton<IAgentRuntimeFactory, AgentFactory>();
builder.Services.AddSingleton<IAgentTurnRunner, AgentRuntimeStore>();

// 将当前 Cesium 场景上下文编译进 LLM prompt
builder.Services.AddSingleton<IScenePromptBuilder, ScenePromptBuilder>();

// 对话服务：Scoped——每个 HTTP 请求一个实例，便于请求级状态与依赖生命周期对齐
builder.Services.AddScoped<IChatService, ChatService>();

// 仅开发环境使用：允许前端 Vite（5173）跨域访问本 API
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        DevelopmentCorsPolicy,
        policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// =============================================================================
// 构建应用并配置请求管道
// =============================================================================
var app = builder.Build();

// 最先处理转发头，确保后续中间件看到的是真实协议（尤其是反向代理后的 HTTPS）
app.UseForwardedHeaders();
app.UseHttpsRedirection();

// CORS 仅在 Development 启用，生产应由同源部署或网关处理跨域
if (app.Environment.IsDevelopment())
{
    app.UseCors(DevelopmentCorsPolicy);
}

// 属性路由控制器（如 ChatController → POST /api/chat）
app.MapControllers();

// 存活/就绪探测：不调用 LLM 或 Astrox，配置合法即可返回 Healthy
app.MapHealthChecks("/healthz");

app.Run();

// 供集成测试（WebApplicationFactory）引用入口程序集；top-level statements 下需 partial Program
public partial class Program;

using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Services;
using CesiumAI.Api.Tools;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

const string DevelopmentCorsPolicy = "DevelopmentFrontend";
const string AstroxRawClient = "AstroxRaw";

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services
    .AddOptions<ChatEndpointOptions>()
    .Bind(builder.Configuration.GetSection(ChatEndpointOptions.SectionName))
    .Validate(
        options => options.Timeout > TimeSpan.Zero,
        "ChatEndpoint:Timeout must be greater than zero.")
    .ValidateOnStart();
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
builder.Services
    .AddOptions<SkillsOptions>()
    .Bind(builder.Configuration.GetSection(SkillsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SkillsOptions>, SkillsOptionsValidator>();

builder.Services
    .AddHttpClient<IAstroxClient, AstroxClient>((services, client) =>
    {
        client.BaseAddress = services.GetRequiredService<IOptions<AstroxOptions>>().Value.BaseUrl;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    });
builder.Services
    .AddHttpClient(AstroxRawClient, (services, client) =>
    {
        client.BaseAddress = services.GetRequiredService<IOptions<AstroxOptions>>().Value.BaseUrl;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOrbitScenarioService, OrbitScenarioService>();
builder.Services.AddSingleton<AstroxRawTools>(services =>
    new AstroxRawTools(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(AstroxRawClient),
        services.GetRequiredService<IOptions<AstroxOptions>>()));
builder.Services.AddSingleton<IAgentRuntimeFactory, AgentFactory>();
builder.Services.AddSingleton<IAgentTurnRunner, AgentRuntimeStore>();
builder.Services.AddSingleton<IScenePromptBuilder, ScenePromptBuilder>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        DevelopmentCorsPolicy,
        policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevelopmentCorsPolicy);
}

app.MapControllers();
app.MapHealthChecks("/healthz");

app.Run();

public partial class Program;

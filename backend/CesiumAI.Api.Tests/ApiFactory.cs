using CesiumAI.Api.Configuration;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CesiumAI.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private static readonly TimeSpan IntegrationTimeout = TimeSpan.FromMilliseconds(25);
    private readonly string _environment;
    private readonly string _skillsDirectory;
    private readonly bool _ownsSkillsDirectory;

    public ApiFactory()
        : this(
            Environments.Development,
            Directory.CreateTempSubdirectory("cesiumai-test-skills-").FullName,
            ownsSkillsDirectory: true)
    {
    }

    internal ApiFactory(string environment)
        : this(
            environment,
            Directory.CreateTempSubdirectory("cesiumai-test-skills-").FullName,
            ownsSkillsDirectory: true)
    {
    }

    internal ApiFactory(string environment, string skillsDirectory)
        : this(environment, skillsDirectory, ownsSkillsDirectory: false)
    {
    }

    private ApiFactory(
        string environment,
        string skillsDirectory,
        bool ownsSkillsDirectory)
    {
        _environment = environment;
        _skillsDirectory = skillsDirectory;
        _ownsSkillsDirectory = ownsSkillsDirectory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((context, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ApiKey"] = "integration-test-key",
                ["Skills:Path"] = Path.GetRelativePath(
                    context.HostingEnvironment.ContentRootPath,
                    _skillsDirectory),
                [$"{ChatEndpointOptions.SectionName}:Timeout"] =
                    IntegrationTimeout.ToString("c")
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatService>();
            services.AddSingleton<IChatService, FakeChatService>();
            services.AddSingleton<IStartupFilter, ClientCancellationStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && _ownsSkillsDirectory && Directory.Exists(_skillsDirectory))
        {
            Directory.Delete(_skillsDirectory, recursive: true);
        }
    }

    private sealed class FakeChatService : IChatService
    {
        public async Task<ChatResponse> ChatAsync(
            ChatRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Message == "触发超时")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The timeout delay unexpectedly completed.");
            }

            if (request.Message == "触发无关取消")
            {
                await Task.Delay(IntegrationTimeout * 4, CancellationToken.None);
                throw new OperationCanceledException();
            }

            return new ChatResponse(
                "test-session",
                "已清空场景。",
                [new ClearSceneOp()]);
        }
    }

    private sealed class ClientCancellationStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Headers.ContainsKey("X-Test-Client-Cancelled"))
                    {
                        context.RequestAborted = new CancellationToken(canceled: true);
                    }

                    await nextMiddleware();
                });

                next(app);
            };
    }
}

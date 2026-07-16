using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CesiumAI.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ApiKey"] = "integration-test-key"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatService>();
            services.AddSingleton<IChatService, FakeChatService>();
            services.AddSingleton<IStartupFilter, ClientCancellationStartupFilter>();
        });
    }

    private sealed class FakeChatService : IChatService
    {
        public Task<ChatResponse> ChatAsync(
            ChatRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Message == "触发超时")
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(
                new ChatResponse(
                    "test-session",
                    "已清空场景。",
                    [new ClearSceneOp()]));
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

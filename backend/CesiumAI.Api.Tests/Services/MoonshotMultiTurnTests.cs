using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using OpenAI;

namespace CesiumAI.Api.Tests.Services;

/// <summary>
/// 多轮回归：真实 OpenAI SDK + Harness 管线，对端是按 Moonshot（kimi-k2.6）实际行为编写的假 HTTP 服务：
/// 流式首个分片 content 为空串、随后是 reasoning_content，再输出正文或 tool_calls；
/// 请求校验与 Moonshot 一致——历史中出现空文本片段或未应答的 tool_call 时返回 400。
/// </summary>
public sealed class MoonshotMultiTurnTests : IDisposable
{
    private readonly string _parent;
    private readonly string _contentRoot;

    public MoonshotMultiTurnTests()
    {
        _parent = Directory.CreateTempSubdirectory().FullName;
        _contentRoot = Directory.CreateDirectory(Path.Combine(_parent, "api")).FullName;
        Directory.CreateDirectory(Path.Combine(_contentRoot, "skills"));
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);

    [Fact]
    public async Task ThreeTurns_AfterToolCallTurn_LaterTurnsStillSucceed()
    {
        var llm = new FakeMoonshot();
        var store = new AgentRuntimeStore(CreateFactory(llm));

        TurnResult first = await RunTurnAsync(store, "session", "创建900km高度太阳同步轨道");
        first.ToolCalls.Should().ContainSingle().Which.Should().Be("ClearScene");
        first.SceneOps.Should().ContainSingle().Which.Should().BeOfType<ClearSceneOp>();
        first.Text.Should().Be("已创建。");

        TurnResult second = await RunTurnAsync(store, "session", "典型星链卫星的轨道");
        second.Text.Should().Be("已回答。");

        TurnResult third = await RunTurnAsync(store, "session", "星链卫星轨道高度？");
        third.Text.Should().Be("已回答。");

        llm.Rejections.Should().BeEmpty();
        llm.Requests.Last()["messages"]!.AsArray()
            .Count(message => (string?)message!["role"] == "user")
            .Should().Be(3, "后续轮次必须携带完整的同会话历史");
    }

    [Fact]
    public async Task TurnFailingMidToolLoop_DoesNotPoisonLaterTurnsOfSameSession()
    {
        var llm = new FakeMoonshot { FailRequestsAfterToolResult = 1 };
        var store = new AgentRuntimeStore(CreateFactory(llm));

        Func<Task> failing = () => RunTurnAsync(store, "session", "创建900km高度太阳同步轨道");
        await failing.Should().ThrowAsync<ClientResultException>();

        TurnResult next = await RunTurnAsync(store, "session", "典型星链卫星的轨道");

        next.Text.Should().Be("已回答。");
        llm.Rejections.Should().BeEmpty();
    }

    private static async Task<TurnResult> RunTurnAsync(AgentRuntimeStore store, string sessionId, string prompt)
    {
        var collector = new SceneOpCollector();
        var text = new StringBuilder();
        var toolCalls = new List<string>();

        await foreach (AgentResponseUpdate update in store.RunStreamingAsync(
            sessionId,
            prompt,
            collector,
            CancellationToken.None))
        {
            foreach (AIContent content in update.Contents)
            {
                switch (content)
                {
                    case TextContent textContent:
                        text.Append(textContent.Text);
                        break;
                    case FunctionCallContent call:
                        toolCalls.Add(call.Name);
                        break;
                }
            }
        }

        return new TurnResult(text.ToString(), toolCalls, collector.Drain());
    }

    private AgentFactory CreateFactory(FakeMoonshot llm)
    {
        var rawClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))));
        var rawTools = new AstroxRawTools(
            rawClient,
            Options.Create(new AstroxOptions { BaseUrl = new Uri("https://astrox.example/") }));

        return new AgentFactory(
            () => new OpenAIClient(
                    new ApiKeyCredential("test-key"),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri("https://llm.example/v1"),
                        Transport = new HttpClientPipelineTransport(new HttpClient(llm)),
                        RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
                    })
                .GetChatClient("kimi-k2.6")
                .AsIChatClient(),
            Options.Create(new SkillsOptions { Path = "skills" }),
            new StubHostEnvironment(_contentRoot),
            new UnusedOrbitScenarioService(),
            new SceneStyleValidator(),
            rawTools,
            NullLoggerFactory.Instance);
    }

    private sealed record TurnResult(string Text, IReadOnlyList<string> ToolCalls, IReadOnlyList<SceneOp> SceneOps);

    private sealed class FakeMoonshot : HttpMessageHandler
    {
        private int _toolCallCounter;

        public List<JsonNode> Requests { get; } = [];

        public List<string> Rejections { get; } = [];

        /// <summary>工具结果回传后的前 N 次请求返回 500，模拟工具循环中途失败。</summary>
        public int FailRequestsAfterToolResult { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            JsonNode body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            Requests.Add(body);
            JsonArray messages = body["messages"]!.AsArray();

            string? rejection = Validate(messages);
            if (rejection is not null)
            {
                Rejections.Add(rejection);
                return Error(HttpStatusCode.BadRequest, rejection);
            }

            JsonNode last = messages[^1]!;
            if ((string?)last["role"] == "tool")
            {
                if (FailRequestsAfterToolResult > 0)
                {
                    FailRequestsAfterToolResult--;
                    return Error(HttpStatusCode.InternalServerError, "upstream failure");
                }

                return Stream(TextChunks("已创建。"));
            }

            string userText = ReadText(last);
            if (userText.Contains("创建", StringComparison.Ordinal))
            {
                int index = _toolCallCounter++;
                return Stream(ToolCallChunks($"ClearScene:{index}", "ClearScene"));
            }

            return Stream(TextChunks("已回答。"));
        }

        /// <summary>与 Moonshot 返回的 400 文案一致的两类历史校验。</summary>
        private static string? Validate(JsonArray messages)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                JsonNode message = messages[i]!;
                if (message["content"] is JsonArray parts
                    && parts.Any(part => (string?)part!["type"] == "text"
                        && string.IsNullOrEmpty((string?)part["text"])))
                {
                    return "Invalid request: text content is empty";
                }

                if (message["tool_calls"] is JsonArray calls && calls.Count > 0)
                {
                    var answered = new HashSet<string>();
                    for (int j = i + 1; j < messages.Count && (string?)messages[j]!["role"] == "tool"; j++)
                    {
                        answered.Add((string)messages[j]!["tool_call_id"]!);
                    }

                    string[] missing = calls
                        .Select(call => (string)call!["id"]!)
                        .Where(id => !answered.Contains(id))
                        .ToArray();
                    if (missing.Length > 0)
                    {
                        return "Invalid request: an assistant message with 'tool_calls' must be followed by tool messages "
                            + $"responding to each 'tool_call_id'. The following tool_call_ids did not have response messages: {string.Join(", ", missing)}";
                    }
                }
            }

            return null;
        }

        private static string ReadText(JsonNode message) =>
            message["content"] switch
            {
                JsonValue value => (string?)value ?? string.Empty,
                JsonArray parts => string.Concat(parts.Select(part => (string?)part!["text"])),
                _ => string.Empty
            };

        private static IEnumerable<JsonObject> Prelude() =>
        [
            Delta(new JsonObject { ["role"] = "assistant", ["content"] = "" }),
            Delta(new JsonObject { ["reasoning_content"] = "思考" }),
            Delta(new JsonObject { ["reasoning_content"] = "中" })
        ];

        private static IEnumerable<JsonObject> TextChunks(string text) =>
        [
            .. Prelude(),
            .. text.Select(ch => Delta(new JsonObject { ["content"] = ch.ToString() })),
            Delta(new JsonObject(), "stop")
        ];

        private static IEnumerable<JsonObject> ToolCallChunks(string callId, string name) =>
        [
            .. Prelude(),
            Delta(new JsonObject
            {
                ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["id"] = callId,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = name, ["arguments"] = "{}" }
                })
            }),
            Delta(new JsonObject(), "tool_calls")
        ];

        private static JsonObject Delta(JsonObject delta, string? finishReason = null) =>
            new()
            {
                ["id"] = "chatcmpl-test",
                ["object"] = "chat.completion.chunk",
                ["created"] = 1_791_295_602,
                ["model"] = "kimi-k2.6",
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = delta,
                    ["finish_reason"] = finishReason
                })
            };

        private static HttpResponseMessage Stream(IEnumerable<JsonObject> chunks)
        {
            var sse = new StringBuilder();
            foreach (JsonObject chunk in chunks)
            {
                sse.Append("data: ").Append(chunk.ToJsonString()).Append("\n\n");
            }

            sse.Append("data: [DONE]\n\n");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream")
            };
        }

        private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
            StubHttpMessageHandler.Json(
                status,
                JsonSerializer.Serialize(new { error = new { message, type = "invalid_request_error" } }));
    }

    private sealed class UnusedOrbitScenarioService : IOrbitScenarioService
    {
        public Task<JsonElement> CreateSsoJ2PacketAsync(SsoJ2Scenario scenario, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("测试不应调用 Astrox。");

        public Task<JsonElement> CreatePacketFromPropagationAsync(
            string id,
            string name,
            string propagatorPath,
            JsonElement request,
            DateTimeOffset startUtc,
            DateTimeOffset stopUtc,
            string? orbitHint,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("测试不应调用 Astrox。");

        public JsonElement CreatePacketFromPositions(
            string id,
            string name,
            JsonElement position,
            DateTimeOffset startUtc,
            DateTimeOffset stopUtc,
            string? orbitHint) =>
            throw new InvalidOperationException("测试不应调用 Astrox。");
    }

    private sealed class StubHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CesiumAI.Api.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

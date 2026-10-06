using System.Text.Json.Serialization;

namespace CesiumAI.Api.Models;

/// <summary>
/// <c>POST /api/chat/stream</c> 的 SSE 事件。<see cref="EventName"/> 写入 SSE 的 <c>event:</c> 字段，
/// 记录本身序列化为 <c>data:</c> 的单行 JSON。
/// 顺序约定：session → (delta | tool_call | tool_result)* → done | error；done / error 之后不再有事件。
/// </summary>
public abstract record ChatStreamEvent
{
    [JsonIgnore]
    public abstract string EventName { get; }
}

/// <summary>首个事件：本轮使用的会话 ID（请求未带时由服务端新建）。</summary>
public sealed record ChatSessionStreamEvent(
    [property: JsonPropertyName("sessionId")] string SessionId) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventName => "session";
}

/// <summary>助手文本增量；按顺序拼接即为完整回复。</summary>
public sealed record ChatDeltaStreamEvent(
    [property: JsonPropertyName("text")] string Text) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventName => "delta";
}

/// <summary>模型发起了一次工具调用（仅用于进度展示，不携带参数）。</summary>
public sealed record ChatToolCallStreamEvent(
    [property: JsonPropertyName("callId")] string CallId,
    [property: JsonPropertyName("name")] string Name) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventName => "tool_call";
}

/// <summary>工具调用结束（不携带结果内容，避免把大型星历等推给浏览器）。</summary>
public sealed record ChatToolResultStreamEvent(
    [property: JsonPropertyName("callId")] string CallId,
    [property: JsonPropertyName("succeeded")] bool Succeeded) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventName => "tool_result";
}

/// <summary>
/// 最终事件，形状与非流式 <see cref="ChatResponse"/> 一致。
/// <c>message</c> 等于全部 delta 文本的拼接；<c>sceneOps</c> 仅在本轮成功结束时一次性下发。
/// </summary>
public sealed record ChatDoneStreamEvent(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("sceneOps")] IReadOnlyList<SceneOp> SceneOps) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventName => "done";
}

/// <summary>流已开始（HTTP 200 已发出）后发生的失败，如 agent_timeout / agent_error。</summary>
public sealed record ChatErrorStreamEvent(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("detail")] string Detail) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventName => "error";
}

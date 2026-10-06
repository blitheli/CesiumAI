using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CesiumAI.Api.Services;

/// <summary>
/// 位于 Harness 管线最内层、紧贴 OpenAI 兼容客户端：每次发往 LLM 前清洗会话历史，使其满足 Moonshot 等服务端的严格校验。
/// <list type="bullet">
/// <item>去掉空文本片段。kimi 流式首个分片 content 为空串，随后的 reasoning_content 把正文隔开，
/// 历史中的助手消息因此含空 <see cref="TextContent"/>，序列化为 <c>{"type":"text","text":""}</c> 后被拒绝（text content is empty）。</item>
/// <item>去掉未应答的工具调用及孤立的工具结果。工具循环中途失败时 Harness 已逐次持久化带 tool_calls 的助手消息，
/// 却没有对应的 tool 消息，此后同一会话每轮都会被拒绝（tool_call_ids did not have response messages）。</item>
/// </list>
/// 只改写本次请求的消息副本，不修改会话中持久化的历史。
/// </summary>
internal sealed class ChatHistorySanitizingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Sanitize(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Sanitize(messages), options, cancellationToken);

    internal static List<ChatMessage> Sanitize(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> source = messages.ToList();
        var result = new List<ChatMessage>(source.Count);
        // 上一条助手消息中、已被紧随其后的 tool 消息应答的调用 ID。
        var answerableCallIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < source.Count; i++)
        {
            ChatMessage message = source[i];

            if (message.Role == ChatRole.Assistant)
            {
                HashSet<string> answered = CollectAnsweredCallIds(source, i + 1);
                answerableCallIds = answered;
                AppendIfNotEmpty(result, message, content => content switch
                {
                    TextContent text => !string.IsNullOrEmpty(text.Text),
                    FunctionCallContent call => answered.Contains(call.CallId),
                    _ => true
                });
                continue;
            }

            if (message.Role == ChatRole.Tool)
            {
                HashSet<string> answerable = answerableCallIds;
                AppendIfNotEmpty(result, message, content => content switch
                {
                    TextContent text => !string.IsNullOrEmpty(text.Text),
                    FunctionResultContent functionResult => answerable.Contains(functionResult.CallId),
                    _ => true
                });
                continue;
            }

            answerableCallIds = [];
            AppendIfNotEmpty(result, message, content =>
                content is not TextContent text || !string.IsNullOrEmpty(text.Text));
        }

        return result;
    }

    private static HashSet<string> CollectAnsweredCallIds(List<ChatMessage> messages, int start)
    {
        var answered = new HashSet<string>(StringComparer.Ordinal);
        for (int j = start; j < messages.Count && messages[j].Role == ChatRole.Tool; j++)
        {
            foreach (FunctionResultContent result in messages[j].Contents.OfType<FunctionResultContent>())
            {
                answered.Add(result.CallId);
            }
        }

        return answered;
    }

    private static void AppendIfNotEmpty(
        List<ChatMessage> result,
        ChatMessage message,
        Func<AIContent, bool> keep)
    {
        if (message.Contents.All(keep))
        {
            if (HasSendableContent(message.Contents))
            {
                result.Add(message);
            }

            return;
        }

        List<AIContent> kept = message.Contents.Where(keep).ToList();
        if (!HasSendableContent(kept))
        {
            return;
        }

        ChatMessage copy = message.Clone();
        copy.Contents = kept;
        result.Add(copy);
    }

    // 推理内容不会被序列化发送；只剩推理内容的消息等同空消息。
    private static bool HasSendableContent(IEnumerable<AIContent> contents) =>
        contents.Any(content => content is not TextReasoningContent);
}

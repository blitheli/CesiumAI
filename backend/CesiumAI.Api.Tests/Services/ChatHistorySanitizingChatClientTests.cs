using CesiumAI.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace CesiumAI.Api.Tests.Services;

public class ChatHistorySanitizingChatClientTests
{
    [Fact]
    public void Sanitize_RemovesEmptyTextParts_AndKeepsNonEmptyText()
    {
        var assistant = new ChatMessage(
            ChatRole.Assistant,
            [new TextContent(""), new TextReasoningContent("思考"), new TextContent("已创建。")]);

        List<ChatMessage> result = ChatHistorySanitizingChatClient.Sanitize(
            [new ChatMessage(ChatRole.User, "创建"), assistant]);

        result.Should().HaveCount(2);
        result[1].Contents.OfType<TextContent>().Select(text => text.Text)
            .Should().Equal("已创建。");
        assistant.Contents.Should().HaveCount(3, "不得修改会话中持久化的原消息");
    }

    [Fact]
    public void Sanitize_DropsMessagesLeftWithOnlyReasoningOrNothing()
    {
        List<ChatMessage> result = ChatHistorySanitizingChatClient.Sanitize(
        [
            new ChatMessage(ChatRole.User, "你好"),
            new ChatMessage(ChatRole.Assistant, [new TextContent(""), new TextReasoningContent("思考")]),
            new ChatMessage(ChatRole.User, "再问")
        ]);

        result.Select(message => message.Role).Should().Equal(ChatRole.User, ChatRole.User);
    }

    [Fact]
    public void Sanitize_RemovesUnansweredToolCalls_AndKeepsAnsweredOnes()
    {
        List<ChatMessage> result = ChatHistorySanitizingChatClient.Sanitize(
        [
            new ChatMessage(ChatRole.User, "创建"),
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("a", "ClearScene"), new FunctionCallContent("b", "AddSatelliteJ2")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("a", "ok")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c", "PropagateAndAddSatellite")]),
            new ChatMessage(ChatRole.User, "下一轮")
        ]);

        result.Select(message => message.Role)
            .Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User);
        result[1].Contents.OfType<FunctionCallContent>().Select(call => call.CallId)
            .Should().Equal("a");
    }

    [Fact]
    public void Sanitize_RemovesOrphanToolResults()
    {
        List<ChatMessage> result = ChatHistorySanitizingChatClient.Sanitize(
        [
            new ChatMessage(ChatRole.User, "创建"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("x", "ok")]),
            new ChatMessage(ChatRole.User, "下一轮")
        ]);

        result.Select(message => message.Role).Should().Equal(ChatRole.User, ChatRole.User);
    }

    [Fact]
    public void Sanitize_ReturnsSameInstances_WhenHistoryIsAlreadyValid()
    {
        ChatMessage[] history =
        [
            new ChatMessage(ChatRole.System, "规则"),
            new ChatMessage(ChatRole.User, "创建"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("a", "ClearScene")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("a", "ok")]),
            new ChatMessage(ChatRole.Assistant, "完成")
        ];

        ChatHistorySanitizingChatClient.Sanitize(history).Should().Equal(history);
    }
}

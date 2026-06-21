namespace Liuvis.Web.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// 保存 Design Studio 页面状态，使其在路由切换（离开/返回）后不会重置。
/// Scoped 生命周期，每个 Blazor Circuit 一个实例。
/// </summary>
public class DesignStudioState
{
    public bool IsInitialized { get; set; }

    public Guid SessionId { get; set; }

    public string ChatInput { get; set; } = "Create a blue cube";

    public string Response { get; set; } = "";

    public string Thinking { get; set; } = "";

    public string StreamingResponse { get; set; } = "";

    public string Progress { get; set; } = "Thinking...";

    public string? ModelUrl { get; set; }

    public string? SceneData { get; set; }

    public bool Sending { get; set; }

    public List<ChatEntry> ChatHistory { get; set; } = new();

    public void Reset()
    {
        IsInitialized = false;
        SessionId = Guid.Empty;
        ChatInput = "Create a blue cube";
        Response = "";
        Thinking = "";
        StreamingResponse = "";
        Progress = "Thinking...";
        ModelUrl = null;
        SceneData = null;
        Sending = false;
        ChatHistory.Clear();
    }

    public record ChatEntry(string Text, bool IsUser);
}

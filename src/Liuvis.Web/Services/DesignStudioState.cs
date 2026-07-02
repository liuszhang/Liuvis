namespace Liuvis.Web.Services;

using System;
using System.Collections.Generic;
using Liuvis.Core.Enums;
using Liuvis.Web.Models;

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

    public Guid? ModelId { get; set; }

    public string? SceneData { get; set; }

    public bool IsStl { get; set; }

    public bool Sending { get; set; }

    public ModelFormat OutputFormat { get; set; } = ModelFormat.GLB;

    public List<ChatEntry> ChatHistory { get; set; } = new();

    /// <summary>Components of the currently loaded model.</summary>
    public List<ComponentVm> Components { get; set; } = new();

    /// <summary>Per-component triangle counts for STL mesh splitting in JS. Ordered same as Components.</summary>
    public List<int> ComponentTriangleCounts { get; set; } = new();

    /// <summary>Currently selected component ID in the tree.</summary>
    public string? SelectedComponentId { get; set; }

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
        ModelId = null;
        SceneData = null;
        IsStl = false;
        Sending = false;
        OutputFormat = ModelFormat.GLB;
        ChatHistory.Clear();
        Components.Clear();
        ComponentTriangleCounts.Clear();
        SelectedComponentId = null;
    }

    public record ChatEntry(string Text, bool IsUser);
}

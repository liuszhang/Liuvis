using CJCore.Agent.Abstractions;

namespace Liuvis.Agent;

/// <summary>
/// Liuvis 三维模型生成 Agent 配置，实现 CJCore 的 IAgentConfig 接口。
/// 包含 Agent 基础参数（名称、SystemPrompt、模型、温度等）以及本体接入专属配置。
/// </summary>
public class ModelingAgentConfig : IAgentConfig
{
    // ======== IAgentConfig 实现 ========

    /// <summary>Agent 唯一名称标识。</summary>
    public string AgentName { get; set; } = "Liuvis.ModelingAgent";

    /// <summary>Agent 功能描述。</summary>
    public string Description { get; set; } = "Liuvis 三维模型生成 Agent，结合本体数据驱动生成符合企业规范的三维模型。";

    /// <summary>系统提示词，在 Agent 初始化时组装（角色 + 本体概要占位 + 技能清单）。</summary>
    public string SystemPrompt { get; set; } =
        "你是一个专业的三维模型生成助手。你能够根据用户的自然语言描述，结合本体数据中定义的对象模型、约束和规则，生成符合规范的三维模型。\n\n" +
        "## 工作流程\n" +
        "1. 理解用户的建模需求，提取关键参数和约束条件。\n" +
        "2. 使用可用工具查询本体数据定义、约束和规则。\n" +
        "3. 基于本体定义生成 DesignSpec（包含几何参数、材质、约束等）。\n" +
        "4. 在生成前/后执行规则校验，确保输出符合规范。\n" +
        "5. 调用模型导出工具完成几何构建与文件输出。\n\n" +
        "## 本体上下文概要\n" +
        "{ontologySummary}\n\n" +
        "## 可用技能\n" +
        "{skillManifest}\n";

    /// <summary>指定模型名。null 时使用全局默认。</summary>
    public string? Model { get; set; }

    /// <summary>LLM 温度参数（0~2）。</summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>最大工具调用轮次，默认 5。</summary>
    public int MaxToolCallRounds { get; set; } = 5;

    // ======== 阶段一本体接入专属配置 ========

    /// <summary>CJOntology 服务的基础 URL（如 http://localhost:5007）。</summary>
    public string CJOntologyBaseUrl { get; set; } = "http://localhost:5007";

    /// <summary>要接入的本体 ID（对应 CJOntology 中的本体标识）。</summary>
    public string OntologyId { get; set; } = string.Empty;

    /// <summary>本体快照刷新间隔（秒），默认 60 秒。</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;
}

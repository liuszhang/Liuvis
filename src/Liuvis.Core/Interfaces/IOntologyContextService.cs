using System.Text.Json;
using Liuvis.Core.Ontology;

namespace Liuvis.Core.Interfaces;

/// <summary>
/// 本体上下文服务接口，用于从 CJOntology 获取本体数据定义、约束、规则等上下文信息。
/// 阶段一作为接口占位，阶段二实现具体 REST 客户端（进程内缓存 + 定时刷新）。
/// </summary>
public interface IOntologyContextService
{
    /// <summary>获取本体概要摘要（对象类型清单 + 关键约束），用于注入 Agent 系统提示词。</summary>
    Task<string> GetOntologySummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>检查本体数据是否可用（CJOntology 服务可达）。</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>获取 CJOntology 基础 URL。</summary>
    string BaseUrl { get; }

    /// <summary>获取当前使用的本体 ID。</summary>
    string OntologyId { get; }

    /// <summary>获取本体快照原始 JSON（对应 CJOntology api/ontology/{id}/snapshot 的 Data 部分），进程内缓存。</summary>
    Task<JsonElement?> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取元模型导出原始 JSON（对应 CJOntology api/metamodel/export 的 Data 部分，含 M0–M7 各模块 JsonElement?），
    /// 进程内缓存。
    /// </summary>
    Task<JsonElement?> GetMetamodelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取规则集（进程内缓存）。支持按规则类型（Kind：Validation/Derivation/Action，即 CJOntology 的 TargetType 维度）
    /// 与 TargetCode（规则作用的对象/字段代码）过滤。
    /// </summary>
    Task<IReadOnlyList<OntologyRuleInfo>> GetRulesAsync(
        string? targetType = null,
        string? targetCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>对指定实例执行 M3 规则求值（POST CJOntology api/m3/evaluate）。</summary>
    Task<IReadOnlyList<OntologyRuleEvaluationResult>> EvaluateRuleAsync(
        JsonElement instance,
        string? instanceType = null,
        IDictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>获取知识工件元数据索引（仅元数据，不含内容），进程内缓存。</summary>
    Task<IReadOnlyList<OntologyMetaItem>> GetMetadataIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>手动刷新所有缓存（快照 / 规则集 / 知识工件索引）。</summary>
    Task<bool> RefreshAsync(CancellationToken cancellationToken = default);
}

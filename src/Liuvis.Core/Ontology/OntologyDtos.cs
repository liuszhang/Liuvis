namespace Liuvis.Core.Ontology;

/// <summary>
/// 本体规则概要信息（从 CJOntology M3 规则列表端点解析，保持轻量无外部依赖）。
/// </summary>
public sealed record OntologyRuleInfo(
    string Id,
    string Code,
    string Name,
    string Kind,
    string TargetType,
    string TargetCode,
    string Phase,
    string Severity,
    string Status,
    int Priority);

/// <summary>
/// 单条 M3 规则求值结果（对应 CJOntology M3RuleEvaluationResult 的轻量映射）。
/// </summary>
public sealed record OntologyRuleEvaluationResult(
    string RuleId,
    string RuleCode,
    string RuleName,
    bool ConditionMet,
    bool Violated,
    string? Message,
    string Severity,
    string Kind);

/// <summary>
/// 知识工件元数据索引条目（对应 CJOntology MetaDataEntry 的轻量映射，仅缓存元数据不缓存内容）。
/// </summary>
public sealed record OntologyMetaItem(
    string Id,
    string Code,
    string Name,
    string Type,
    string? FolderId,
    string? FolderName,
    string? LibraryName,
    string? Tags);

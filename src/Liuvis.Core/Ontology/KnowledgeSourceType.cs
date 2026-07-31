namespace Liuvis.Core.Ontology;

/// <summary>
/// 知识库条目的来源类型，用于区分知识工件（本体导入）与模型描述（原有生成链路）。
/// 阶段五扩展：支持从 CJOntology MetaData 导入制度/法规/标准/模板索引。
/// </summary>
public enum KnowledgeSourceType
{
    /// <summary>原始模型描述（Liuvis 生成链路产生）。</summary>
    ModelDescription = 0,

    /// <summary>企业制度（enterprise_regulations）。</summary>
    EnterpriseRegulation = 1,

    /// <summary>法律法规（legal_laws）。</summary>
    LegalLaw = 2,

    /// <summary>国家标准（national_standards）。</summary>
    NationalStandard = 3,

    /// <summary>三维模型模板（model_templates）。</summary>
    ModelTemplate = 4,

    /// <summary>文档模板（document_templates）。</summary>
    DocumentTemplate = 5,

    /// <summary>其他本体知识工件。</summary>
    OtherOntology = 99,
}

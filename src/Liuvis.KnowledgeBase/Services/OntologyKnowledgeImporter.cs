using CJCore.CoreLog;
using Liuvis.Core.Interfaces;
using Liuvis.Core.Ontology;
using System.Security.Cryptography;
using System.Text;

namespace Liuvis.KnowledgeBase.Services;

/// <summary>
/// 阶段五：本体知识工件导入器。
/// 从 CJOntology MetaData 拉取知识工件索引（制度/法规/标准/模板），
/// 逐条写入向量库（IVectorSearchService），建立 OntologyCode ↔ 向量存储的关联。
/// 
/// 使用方式：
/// - 启动时调用 ImportAsync() 一次性同步全部知识工件
/// - /api/admin/ontology/refresh 端点触发后调用 ReimportAsync() 增量更新
/// </summary>
public class OntologyKnowledgeImporter
{
    private readonly IOntologyContextService _ontology;
    private readonly IVectorSearchService _vectorSearch;
    private readonly ILlmClient _llmClient;

    /// <summary>
    /// 根据名称生成确定性 Guid（UUID v3 语义：MD5 名称哈希）。
    /// 同一名称始终映射到同一 Guid。
    /// </summary>
    private static Guid CreateDeterministicGuid(string name)
    {
        var nsBytes = Guid.Empty.ToByteArray();
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, buffer, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, buffer, nsBytes.Length, nameBytes.Length);

        var hash = MD5.HashData(buffer);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3 (MD5 namespace-based)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant

        return new Guid(hash);
    }

    /// <summary>Type 到 KnowledgeSourceType 的映射表。</summary>
    private static readonly Dictionary<string, KnowledgeSourceType> TypeMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enterprise_regulations"] = KnowledgeSourceType.EnterpriseRegulation,
        ["legal_laws"] = KnowledgeSourceType.LegalLaw,
        ["national_standards"] = KnowledgeSourceType.NationalStandard,
        ["model_templates"] = KnowledgeSourceType.ModelTemplate,
        ["document_templates"] = KnowledgeSourceType.DocumentTemplate,
    };

    public OntologyKnowledgeImporter(
        IOntologyContextService ontology,
        IVectorSearchService vectorSearch,
        ILlmClient llmClient)
    {
        _ontology = ontology;
        _vectorSearch = vectorSearch;
        _llmClient = llmClient;
    }

    /// <summary>从 CJOntology 全量导入知识工件到向量库。</summary>
    /// <returns>成功导入的条目数。</returns>
    public async Task<int> ImportAsync(CancellationToken ct = default)
    {
        CJLog.Information("OntologyKnowledgeImporter: starting full import", source: "OntologyKnowledgeImporter");

        if (!await _ontology.IsAvailableAsync(ct))
        {
            CJLog.Warning("CJOntology not available, skipping knowledge import", source: "OntologyKnowledgeImporter");
            return 0;
        }

        var items = await _ontology.GetMetadataIndexAsync(ct);
        if (items == null || items.Count == 0)
        {
            CJLog.Information("No metadata items to import", source: "OntologyKnowledgeImporter");
            return 0;
        }

        var imported = 0;
        foreach (var item in items)
        {
            try
            {
                var sourceType = ResolveSourceType(item.Type);
                var category = sourceType switch
                {
                    KnowledgeSourceType.EnterpriseRegulation => "企业制度",
                    KnowledgeSourceType.LegalLaw => "法律法规",
                    KnowledgeSourceType.NationalStandard => "国家标准",
                    KnowledgeSourceType.ModelTemplate => "模型模板",
                    KnowledgeSourceType.DocumentTemplate => "文档模板",
                    _ => "本体知识"
                };

                var description = BuildDescription(item);

                // 使用 LLM 生成语义 embedding
                var embedding = await _llmClient.GetEmbeddingAsync(description, ct);

                // 生成确定性 GUID（用 item.Code 作为 UUID v3 种子）
                var entityId = CreateDeterministicGuid(item.Code ?? item.Name);

                // 构建元数据字典，包含 OntologyCode 和 SourceType
                var metadata = new Dictionary<string, string>
                {
                    ["Category"] = category,
                    ["Description"] = description,
                    ["Tags"] = string.Join(",", ExtractTags(item)),
                    ["OntologyCode"] = item.Code ?? "",
                    ["SourceType"] = sourceType.ToString(),
                    ["Name"] = item.Name
                };
                if (!string.IsNullOrWhiteSpace(item.FolderName))
                    metadata["FolderName"] = item.FolderName;
                if (!string.IsNullOrWhiteSpace(item.LibraryName))
                    metadata["LibraryName"] = item.LibraryName;

                await _vectorSearch.Upsert(entityId, embedding, metadata, ct);
                imported++;

                CJLog.Information($"Imported: {item.Code} ({item.Name})", source: "OntologyKnowledgeImporter");
            }
            catch (Exception ex)
            {
                CJLog.Error(ex, $"Failed to import metadata item {item.Code}", source: "OntologyKnowledgeImporter");
            }
        }

        CJLog.Information($"OntologyKnowledgeImporter: imported {imported}/{items.Count} items",
            source: "OntologyKnowledgeImporter");
        return imported;
    }

    private static KnowledgeSourceType ResolveSourceType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return KnowledgeSourceType.OtherOntology;

        return TypeMapping.TryGetValue(type, out var mapped) ? mapped : KnowledgeSourceType.OtherOntology;
    }

    private static string BuildDescription(OntologyMetaItem item)
    {
        var parts = new List<string> { item.Name };
        if (!string.IsNullOrWhiteSpace(item.FolderName))
            parts.Add($"所属目录: {item.FolderName}");
        if (!string.IsNullOrWhiteSpace(item.LibraryName))
            parts.Add($"来源库: {item.LibraryName}");
        if (!string.IsNullOrWhiteSpace(item.Tags))
            parts.Add($"标签: {item.Tags}");
        return string.Join(" | ", parts);
    }

    private static IEnumerable<string> ExtractTags(OntologyMetaItem item)
    {
        var tags = new List<string> { item.Type ?? "ontology" };
        if (!string.IsNullOrWhiteSpace(item.FolderName))
            tags.Add(item.FolderName);
        if (!string.IsNullOrWhiteSpace(item.LibraryName))
            tags.Add(item.LibraryName);
        if (!string.IsNullOrWhiteSpace(item.Tags))
            tags.AddRange(item.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return tags;
    }
}

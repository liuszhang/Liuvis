namespace Liuvis.Infrastructure.Ontology;

/// <summary>
/// CJOntology 本体接入配置（对应 appsettings 中 Ontology 节）。
/// </summary>
public sealed class OntologyOptions
{
    /// <summary>CJOntology 服务基础 URL，默认 http://localhost:5007。</summary>
    public string BaseUrl { get; set; } = "http://localhost:5007";

    /// <summary>要接入的本体 ID（CJOntology 中本体对象的 Guid）。</summary>
    public string OntologyId { get; set; } = string.Empty;

    /// <summary>本体快照/规则/知识工件缓存刷新间隔（秒），默认 60。</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    /// <summary>知识工件索引最大缓存条数，默认 500。</summary>
    public int MaxMetadataItems { get; set; } = 500;

    /// <summary>HTTP 请求超时（秒），默认 15。</summary>
    public int TimeoutSeconds { get; set; } = 15;
}

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Liuvis.Core.Interfaces;
using Liuvis.Core.Ontology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Liuvis.Infrastructure.Ontology;

/// <summary>
/// CJOntology 本体上下文 REST 客户端。
/// 进程内缓存本体快照 / M3 规则集 / 知识工件元数据索引，定时刷新（默认 60s）并支持手动刷新。
/// 服务不可达时安全降级（返回空值/降级文案），不向调用方抛异常（参照 ABWork HttpCJOntologyClient 容错模式）。
/// </summary>
public sealed class OntologyContextService : IOntologyContextService, IDisposable
{
    private const string SnapshotEndpoint = "api/ontology/{0}/snapshot";
    private const string MetamodelExportEndpoint = "api/metamodel/export";
    private const string RulesEndpoint = "api/m3/rules";
    private const string EvaluateEndpoint = "api/m3/evaluate";
    private const string MetadataEntriesEndpoint = "api/metadata/entries";

    private readonly HttpClient _http;
    private readonly OntologyOptions _options;
    private readonly ILogger<OntologyContextService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Timer? _refreshTimer;
    private bool _disposed;

    // ---- 进程内缓存（_snapshot/_metamodel 由 _cacheGate 保护；其余引用类型 volatile 保证跨线程可见） ----
    private JsonElement? _snapshot;
    private JsonElement? _metamodel;
    private DateTime _snapshotFetchedAt = DateTime.MinValue;
    private readonly object _cacheGate = new();
    private volatile IReadOnlyList<OntologyRuleInfo> _rules = Array.Empty<OntologyRuleInfo>();
    private readonly Dictionary<string, List<OntologyRuleInfo>> _rulesByTarget = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<OntologyMetaItem> _metadataIndex = Array.Empty<OntologyMetaItem>();
    private volatile string _summary = string.Empty;

    public OntologyContextService(
        HttpClient http,
        IOptions<OntologyOptions> options,
        ILogger<OntologyContextService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        var interval = Math.Max(10, _options.RefreshIntervalSeconds);
        _refreshTimer = new Timer(
            _ => _ = SafeRefreshAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(interval),
            TimeSpan.FromSeconds(interval));
    }

    public string BaseUrl => _options.BaseUrl;

    public string OntologyId => _options.OntologyId;

    // ---------------------------------------------------------------------
    // 接口实现
    // ---------------------------------------------------------------------

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _http.GetAsync(MetadataEntriesEndpoint, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CJOntology 不可达（{BaseUrl}）：{Message}", _options.BaseUrl, ex.Message);
            return false;
        }
    }

    public async Task<JsonElement?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot is null)
            await TryFetchSnapshotAsync(cancellationToken);
        return _snapshot;
    }

    public async Task<JsonElement?> GetMetamodelAsync(CancellationToken cancellationToken = default)
    {
        if (_metamodel is null)
            await TryFetchMetamodelAsync(cancellationToken);
        lock (_cacheGate)
            return _metamodel;
    }

    public async Task<string> GetOntologySummaryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_summary))
            await TryFetchAllAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(_summary) ? BuildDegradedSummary() : _summary;
    }

    public async Task<IReadOnlyList<OntologyRuleInfo>> GetRulesAsync(
        string? targetType = null,
        string? targetCode = null,
        CancellationToken cancellationToken = default)
    {
        if (_rules.Count == 0)
            await TryFetchRulesAsync(cancellationToken);

        IEnumerable<OntologyRuleInfo> result = _rules;
        if (!string.IsNullOrWhiteSpace(targetType))
        {
            var key = targetType.Trim();
            if (_rulesByTarget.TryGetValue(key, out var byTarget))
                result = result.Intersect(byTarget);
            else
                result = result.Where(r => string.Equals(r.TargetType, key, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(targetCode))
        {
            var code = targetCode.Trim();
            result = result.Where(r => string.Equals(r.TargetCode, code, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }

    public async Task<IReadOnlyList<OntologyRuleEvaluationResult>> EvaluateRuleAsync(
        JsonElement instance,
        string? instanceType = null,
        IDictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new JsonObject
            {
                ["instance"] = JsonNode.Parse(instance.GetRawText()) ?? JsonValue.Create(instance),
            };
            if (!string.IsNullOrWhiteSpace(instanceType))
                body["instanceType"] = instanceType;
            if (parameters is { Count: > 0 })
            {
                var paramsObj = new JsonObject();
                foreach (var kv in parameters)
                    paramsObj[kv.Key] = JsonNode.Parse(kv.Value.GetRawText());
                body["params"] = paramsObj;
            }

            using var cts = CreateTimeoutCts(cancellationToken);
            using var response = await _http.PostAsJsonAsync(EvaluateEndpoint, body, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("M3 规则求值失败：HTTP {Status}", (int)response.StatusCode);
                return Array.Empty<OntologyRuleEvaluationResult>();
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var data = ExtractData(root);
            if (data.ValueKind != JsonValueKind.Array)
                return Array.Empty<OntologyRuleEvaluationResult>();

            var results = new List<OntologyRuleEvaluationResult>(data.GetArrayLength());
            foreach (var item in data.EnumerateArray())
            {
                results.Add(new OntologyRuleEvaluationResult(
                    RuleId: GetString(item, "ruleId") ?? GetString(item, "id") ?? string.Empty,
                    RuleCode: GetString(item, "ruleCode") ?? string.Empty,
                    RuleName: GetString(item, "ruleName") ?? string.Empty,
                    ConditionMet: GetBool(item, "conditionMet") ?? false,
                    Violated: GetBool(item, "violated") ?? false,
                    Message: GetString(item, "message"),
                    Severity: GetString(item, "severity") ?? "Unknown",
                    Kind: GetString(item, "kind") ?? "Validation"));
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "M3 规则求值异常（CJOntology 不可达或响应异常）：{Message}", ex.Message);
            return Array.Empty<OntologyRuleEvaluationResult>();
        }
    }

    public async Task<IReadOnlyList<OntologyMetaItem>> GetMetadataIndexAsync(CancellationToken cancellationToken = default)
    {
        if (_metadataIndex.Count == 0)
            await TryFetchMetadataIndexAsync(cancellationToken);
        return _metadataIndex;
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
        => await SafeRefreshAsync(cancellationToken);

    // ---------------------------------------------------------------------
    // 定时刷新
    // ---------------------------------------------------------------------

    private async Task<bool> SafeRefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
            return false;
        try
        {
            var ok = await TryFetchAllAsync(cancellationToken);
            _logger.LogInformation("CJOntology 本体上下文刷新完成：可用={Ok}", ok);
            return ok;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<bool> TryFetchAllAsync(CancellationToken cancellationToken)
    {
        var snapshotOk = await TryFetchSnapshotAsync(cancellationToken);
        var metamodelOk = await TryFetchMetamodelAsync(cancellationToken);
        var rulesOk = await TryFetchRulesAsync(cancellationToken);
        var metadataOk = await TryFetchMetadataIndexAsync(cancellationToken);
        BuildSummary(snapshotOk, metamodelOk);
        return snapshotOk || metamodelOk || rulesOk || metadataOk;
    }

    // ---------------------------------------------------------------------
    // 各数据源拉取
    // ---------------------------------------------------------------------

    private async Task<bool> TryFetchSnapshotAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.OntologyId))
        {
            _logger.LogDebug("OntologyId 未配置，跳过快照拉取");
            return false;
        }

        try
        {
            var endpoint = string.Format(SnapshotEndpoint, _options.OntologyId);
            using var cts = CreateTimeoutCts(cancellationToken);
            using var response = await _http.GetAsync(endpoint, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("本体快照拉取失败：HTTP {Status}（{Endpoint}）", (int)response.StatusCode, endpoint);
                return false;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var data = ExtractData(root);
            _snapshot = data.ValueKind == JsonValueKind.Undefined ? null : data;
            _snapshotFetchedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "本体快照拉取异常：{Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> TryFetchMetamodelAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CreateTimeoutCts(cancellationToken);
            using var response = await _http.GetAsync(MetamodelExportEndpoint, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("元模型导出拉取失败：HTTP {Status}", (int)response.StatusCode);
                return false;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var data = ExtractData(root);
            lock (_cacheGate)
                _metamodel = data.ValueKind == JsonValueKind.Undefined ? null : data;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "元模型导出拉取异常：{Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> TryFetchRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CreateTimeoutCts(cancellationToken);
            using var response = await _http.GetAsync(RulesEndpoint, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("M3 规则集拉取失败：HTTP {Status}", (int)response.StatusCode);
                return false;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var data = ExtractData(root);
            if (data.ValueKind != JsonValueKind.Array)
                return false;

            var rules = new List<OntologyRuleInfo>(data.GetArrayLength());
            var byTarget = new Dictionary<string, List<OntologyRuleInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.EnumerateArray())
            {
                var kind = GetString(item, "kind") ?? "Validation";
                var rule = new OntologyRuleInfo(
                    Id: GetString(item, "id") ?? Guid.Empty.ToString(),
                    Code: GetString(item, "code") ?? string.Empty,
                    Name: GetString(item, "name") ?? string.Empty,
                    Kind: kind,
                    // CJOntology 规则列表端点不返回 TargetType，以 Kind 作为规则目标类型维度
                    TargetType: kind,
                    TargetCode: GetString(item, "targetCode") ?? string.Empty,
                    Phase: GetString(item, "phase") ?? "OnValidate",
                    Severity: GetString(item, "severity") ?? "Unknown",
                    Status: GetString(item, "status") ?? "Active",
                    Priority: GetInt(item, "priority") ?? 50);
                rules.Add(rule);

                if (!byTarget.TryGetValue(kind, out var list))
                {
                    list = new List<OntologyRuleInfo>();
                    byTarget[kind] = list;
                }
                list.Add(rule);
            }

            _rules = rules;
            lock (_rulesByTarget)
            {
                _rulesByTarget.Clear();
                foreach (var kv in byTarget)
                    _rulesByTarget[kv.Key] = kv.Value;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "M3 规则集拉取异常：{Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> TryFetchMetadataIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CreateTimeoutCts(cancellationToken);
            using var response = await _http.GetAsync(MetadataEntriesEndpoint, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("知识工件索引拉取失败：HTTP {Status}", (int)response.StatusCode);
                return false;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var data = ExtractData(root);
            if (data.ValueKind != JsonValueKind.Array)
                return false;

            var max = Math.Max(1, _options.MaxMetadataItems);
            var items = new List<OntologyMetaItem>();
            foreach (var item in data.EnumerateArray())
            {
                if (items.Count >= max)
                    break;
                items.Add(new OntologyMetaItem(
                    Id: GetString(item, "id") ?? Guid.Empty.ToString(),
                    Code: GetString(item, "code") ?? string.Empty,
                    Name: GetString(item, "name") ?? string.Empty,
                    Type: GetString(item, "type") ?? GetString(item, "contentType") ?? "Unknown",
                    FolderId: GetString(item, "folderId"),
                    FolderName: GetString(item, "folderName"),
                    LibraryName: GetString(item, "libraryName"),
                    Tags: GetTags(item)));
            }
            _metadataIndex = items;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "知识工件索引拉取异常：{Message}", ex.Message);
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // 概要生成
    // ---------------------------------------------------------------------

    private void BuildSummary(bool snapshotOk, bool metamodelOk)
    {
        try
        {
            var sb = new StringBuilder(2048);
            JsonElement? snapshot;
            lock (_cacheGate)
                snapshot = _snapshot;
            if (snapshotOk && snapshot is { ValueKind: JsonValueKind.Object })
            {
                var code = GetString(snapshot.Value, "metaObjectCode") ?? GetString(snapshot.Value, "code");
                var name = GetString(snapshot.Value, "metaObjectName") ?? GetString(snapshot.Value, "name");
                var description = GetString(snapshot.Value, "description");
                var version = GetString(snapshot.Value, "version");

                sb.Append("本体: ").Append(name ?? "未知本体");
                if (!string.IsNullOrWhiteSpace(code))
                    sb.Append(" (").Append(code).Append(')');
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(version))
                    sb.Append("版本: ").AppendLine(version);
                if (!string.IsNullOrWhiteSpace(description))
                    sb.Append("描述: ").AppendLine(description);

                var objectTypes = ExtractObjectTypes(snapshot.Value);
                if (objectTypes.Count > 0)
                {
                    sb.Append("对象类型清单（").Append(objectTypes.Count).AppendLine(" 个）:");
                    foreach (var (objCode, objName) in objectTypes)
                        sb.Append("- ").Append(objName).Append(" (").Append(objCode).AppendLine(")");
                }
            }
            else
            {
                sb.AppendLine("（本体快照暂不可用）");
            }

            if (metamodelOk)
            {
                JsonElement? metamodel;
                lock (_cacheGate)
                    metamodel = _metamodel;
                var modules = metamodel is { ValueKind: JsonValueKind.Object }
                    ? metamodel.Value.EnumerateObject().Select(p => p.Name).Where(n => !string.Equals(n, "metaObjectId", StringComparison.OrdinalIgnoreCase)).ToList()
                    : new List<string>();
                sb.Append("元模型模块: ").Append(modules.Count > 0 ? string.Join(", ", modules) : "（已连接，无模块导出）").AppendLine();
            }

            sb.Append("规则集: ").Append(_rules.Count).AppendLine(" 条规则");
            var ruleGroups = _rules.GroupBy(r => r.TargetType);
            foreach (var group in ruleGroups)
                sb.Append("- ").Append(group.Key).Append(": ").Append(group.Count()).AppendLine(" 条");

            sb.Append("知识工件索引: ").Append(_metadataIndex.Count).AppendLine(" 条元数据");

            _summary = sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "本体概要生成失败，使用降级文案");
            _summary = BuildDegradedSummary();
        }
    }

    /// <summary>从快照 ObjectStructure 中启发式提取对象类型（code+name）清单。</summary>
    private static List<(string Code, string Name)> ExtractObjectTypes(JsonElement snapshot)
    {
        var result = new List<(string, string)>();
        if (!snapshot.TryGetProperty("objectStructure", out var structure))
            return result;

        var visited = new HashSet<string>();
        void Walk(JsonElement node, int depth)
        {
            if (depth > 12 || node.ValueKind == JsonValueKind.Undefined)
                return;
            if (node.ValueKind == JsonValueKind.Object)
            {
                var code = GetString(node, "code");
                var name = GetString(node, "name");
                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
                {
                    var key = $"{code}|{name}";
                    if (visited.Add(key))
                        result.Add((code, name));
                }
                foreach (var prop in node.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Walk(prop.Value, depth + 1);
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    Walk(item, depth + 1);
            }
        }

        Walk(structure, 0);
        return result;
    }

    private string BuildDegradedSummary()
        => "（本体上下文服务暂不可用，将以通用模式生成模型。请确认 CJOntology 服务已启动且 appsettings Ontology 节配置正确。）";

    // ---------------------------------------------------------------------
    // 辅助
    // ---------------------------------------------------------------------

    private CancellationTokenSource CreateTimeoutCts(CancellationToken token)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        return cts;
    }

    /// <summary>从 ApiResponse 包装中提取 Data 节点；非包装结构则原样返回。</summary>
    private static JsonElement ExtractData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True)
            return data;

        return root.ValueKind == JsonValueKind.Undefined ? default : root;
    }

    private static string? GetString(JsonElement node, string property)
        => node.ValueKind == JsonValueKind.Object
           && node.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement node, string property)
        => node.ValueKind == JsonValueKind.Object
           && node.TryGetProperty(property, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? GetInt(JsonElement node, string property)
        => node.ValueKind == JsonValueKind.Object
           && node.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static string? GetTags(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("tags", out var tags))
            return null;
        if (tags.ValueKind == JsonValueKind.String)
            return tags.GetString();
        if (tags.ValueKind == JsonValueKind.Array)
        {
            var parts = tags.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String)
                .Select(t => t.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(",", parts);
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refreshTimer?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
        _refreshLock.Dispose();
    }
}

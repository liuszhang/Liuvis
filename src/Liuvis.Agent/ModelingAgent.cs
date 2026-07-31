using CJCore.Agent.Abstractions;
using CJCore.AgentTool.MCPTools;
using CJCore.AgentTool.Skills;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Liuvis.Agent;

public class ModelingAgent : LLMAgentBase<ModelingAgentConfig>, IAgent
{
    private readonly INluService _nluService;
    private readonly IOntologyContextService? _ontologyContextService;
    private readonly SkillRegistry? _skillRegistry;

    public ModelingAgent(
        ModelingAgentConfig config,
        IToolRegistry toolRegistry,
        IServiceScopeFactory scopeFactory,
        INluService nluService,
        IOntologyContextService? ontologyContextService = null,
        SkillRegistry? skillRegistry = null,
        ILogger<ModelingAgent>? logger = null,
        IAgentProgressReporter? progressReporter = null,
        ISessionStore? sessionStore = null,
        IPendingApprovalStore? pendingApprovalStore = null,
        IAgentToolConfigProvider? toolConfigProvider = null,
        IConfirmationGateway? confirmationGateway = null)
        : base(config, toolRegistry, scopeFactory, logger, progressReporter, sessionStore, pendingApprovalStore, toolConfigProvider, confirmationGateway)
    {
        _nluService = nluService ?? throw new ArgumentNullException(nameof(nluService));
        _ontologyContextService = ontologyContextService;
        _skillRegistry = skillRegistry;
    }

    async Task<AgentResponse> IAgent.ExecuteAsync(AgentRequest request, IAgentContext context)
    {
        var userText = request.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userText))
            return AgentResponse.Fail("输入文本为空，无法解析意图。");

        var intentResult = await _nluService.ParseIntent(userText, cancellationToken: context.CancellationToken);
        _logger?.LogInformation("ModelingAgent NLU: {IntentType}", intentResult.IntentType);

        if (intentResult.IntentType != IntentType.Create)
        {
            _logger?.LogInformation("非Create意图({IntentType})，跳过Agent路径", intentResult.IntentType);
            return AgentResponse.Ok(
                data: new { intent = intentResult.IntentType.ToString(), skipReason = "非Create意图，由上层编排处理" },
                message: $"意图类型为 {intentResult.IntentType}，已略过 Agent 增强路径。");
        }

        await InjectOntologyContextAsync(context.CancellationToken);
        InjectSkillManifest();
        return await base.ExecuteAsync(request, context);
    }

    private async Task InjectOntologyContextAsync(CancellationToken ct)
    {
        string ontologySummary;
        if (_ontologyContextService != null && await _ontologyContextService.IsAvailableAsync(ct))
            ontologySummary = await _ontologyContextService.GetOntologySummaryAsync(ct);
        else
            ontologySummary = "（本体上下文服务暂不可用，将以通用模式生成模型。待阶段二接入 CJOntology 后将自动注入本体数据定义、约束与规则。）";

        _config.SystemPrompt = _config.SystemPrompt.Replace("{ontologySummary}", ontologySummary);
    }

    private void InjectSkillManifest()
    {
        string skillManifest;
        if (_skillRegistry != null)
        {
            var manifest = _skillRegistry.GetManifest();
            skillManifest = !string.IsNullOrWhiteSpace(manifest) ? manifest : "（当前无可用技能）";
        }
        else
        {
            skillManifest = "（技能注册表未注入，load_skill 工具不可用。请在 DI 中注册 AddFileSkillSystem。）";
        }

        _config.SystemPrompt = _config.SystemPrompt.Replace("{skillManifest}", skillManifest);
    }
}

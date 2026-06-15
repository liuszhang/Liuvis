# Settings Page — OpenAI / Custom API Key Configuration Fix

## 问题
选中 "Custom API Key" 时配置区域为空，缺少 Base URL 字段，且 OpenAIClient 无法使用运行时设置。

## 修改内容

### 1. `LlmSettings` 模型 (`ISettingsService.cs`)
- 新增 `OpenAIBaseUrl` 属性，默认值 `"https://api.openai.com/v1"`

### 2. `Settings.razor`
- **Render mode**: 改为 `InteractiveServerRenderMode(prerender: false)`，与 DesignStudio 一致，避免 SSR 预渲染状态问题
- **Radio 绑定**: 改为使用本地 `_provider` 字段（非 `_llm.Provider`），确保 radio 切换可靠触发 UI 重渲染
- **新增字段**: Base URL 输入框（带 Link 图标），API Key + Base URL + Model 三栏布局
- **保存逻辑**: `_llm.Provider = _provider` 在保存时同步
- **MudBlazor v8 规范**: `Option` → `option`, `Align` → `align`，消除 analyzer 警告

### 3. `OpenAIClient.cs`
- 构造函数改为直接接收参数：`apiKey`, `baseUrl`, `model`, `embeddingModel`, `maxTokens`, `temperature`, `logger`
- 移除 `IOptions<LlmOptions>` 依赖
- 支持自定义 Base URL：非默认端点时使用 `OpenAIClientOptions.Endpoint`
- 延迟初始化 `OpenAIClient`（lazy `Client` property），适应 OpenAI SDK 2.11.0 的 `ApiKeyCredential`

### 4. `ServiceCollectionExtensions.cs`
- 移除 `services.AddScoped<OpenAIClient>()`
- ILlmClient 工厂直接 new `OpenAIClient(...)` 并传入 SettingsService 的运行时配置
- OpenAI provider 下 API Key/Base URL/Model 均可从 Settings 页面动态配置后生效

## 构建结果
✅ 0 错误 0 警告

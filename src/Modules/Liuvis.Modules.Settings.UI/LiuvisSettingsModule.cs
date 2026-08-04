using CJCore.Framework.Abstractions;

namespace Liuvis.Modules.Settings.UI;

/// <summary>
/// Liuvis Settings 模块 — 实现 IModule 以接入 CJCore 框架的程序集发现 + AppBar 注册。
/// </summary>
public class LiuvisSettingsModule : ModuleBase
{
    public override string Name => "Liuvis.Settings";
}

using CJCore.Framework.Abstractions;

namespace Liuvis.Web.Services;

/// <summary>
/// Liuvis 模块 — 实现 IModule 以接入 CJCore 框架的程序集发现 + AppBar 注册。
/// </summary>
public class LiuvisModule : ModuleBase
{
    public override string Name => "Liuvis";
}

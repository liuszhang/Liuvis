using CJCore.Framework.Abstractions;
using MudBlazor;

namespace Liuvis.Web.Services;

/// <summary>
/// Liuvis 菜单注册 — 将侧边栏导航项接入 CJCore 框架的 CoreMenu。
/// </summary>
public class LiuvisMenuService : IMenuService
{
    public ValueTask<IEnumerable<MenuItem>> GetMenuItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<MenuItem>
        {
            new MenuItem
            {
                Text = "Design Studio",
                Href = "/",
                Icon = Icons.Material.Filled.Dashboard,
                Match = "Exact",
                Order = 10,
                GroupName = "Design Studio"
            },
            new MenuItem
            {
                Text = "My Models",
                Href = "/models",
                Icon = Icons.Material.Filled.ViewInAr,
                Match = "Exact",
                Order = 20,
                GroupName = "Models"
            },
            new MenuItem
            {
                Text = "Search",
                Href = "/models/search",
                Icon = Icons.Material.Filled.Search,
                Match = "Exact",
                Order = 30,
                GroupName = "Models"
            },
            new MenuItem
            {
                Text = "Settings",
                Href = "/settings",
                Icon = Icons.Material.Filled.Settings,
                Match = "Prefix",
                Order = 40,
                GroupName = "System"
            },
        };

        return ValueTask.FromResult<IEnumerable<MenuItem>>(items);
    }
}

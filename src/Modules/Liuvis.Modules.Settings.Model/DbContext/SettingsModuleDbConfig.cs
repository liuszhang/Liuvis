using CJCore.Modules.Data;
using Microsoft.EntityFrameworkCore;

namespace Liuvis.Modules.Settings;

/// <summary>
/// Settings 模块的数据库配置 — 通过 IModuleDbConfig 契约向宿主 DbContext 注册 LlmProvider 实体。
/// </summary>
public class SettingsModuleDbConfig : IModuleDbConfig
{
    public void AddDbSets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LlmProvider>();
    }

    public void ConfigEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LlmProvider>(entity =>
        {
            entity.ToTable("llm_providers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(32);
        });
    }
}

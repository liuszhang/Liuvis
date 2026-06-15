using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Liuvis.Core.Entities;

/// <summary>Persistent application settings key-value store.</summary>
public class AppSetting
{
    [Key]
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    private AppSetting() { }

    public AppSetting(string key, string value, string? description = null)
    {
        Key = key;
        Value = value;
        Description = description;
    }
}

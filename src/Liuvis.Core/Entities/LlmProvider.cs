using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Liuvis.Core.Entities;

/// <summary>
/// Represents a saved LLM provider configuration.
/// Multiple providers can be saved; one is marked as active.
/// </summary>
public class LlmProvider
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Provider { get; set; } = "openai";

    [MaxLength(512)]
    public string? ApiKey { get; set; }

    [MaxLength(512)]
    public string? BaseUrl { get; set; }

    [MaxLength(128)]
    public string? Model { get; set; }

    [MaxLength(512)]
    public string? OllamaUrl { get; set; }

    [MaxLength(128)]
    public string? OllamaModel { get; set; }

    public int MaxTokens { get; set; } = 2000;

    public double Temperature { get; set; } = 0.3;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

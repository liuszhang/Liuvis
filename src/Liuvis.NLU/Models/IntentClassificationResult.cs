namespace Liuvis.NLU.Models;

/// <summary>Result of LLM-based intent classification.</summary>
public class IntentClassificationResult
{
    public string Intent { get; set; } = "Unknown";
    public double Confidence { get; set; }
    public List<EntityResult> Entities { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class EntityResult
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Start { get; set; }
    public int End { get; set; }
}

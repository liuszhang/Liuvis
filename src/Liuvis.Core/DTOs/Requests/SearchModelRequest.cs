namespace Liuvis.Core.DTOs.Requests;

/// <summary>Request to search the knowledge base.</summary>
public class SearchModelRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
}

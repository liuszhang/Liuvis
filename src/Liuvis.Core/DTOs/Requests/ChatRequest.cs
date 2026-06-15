namespace Liuvis.Core.DTOs.Requests;

public class ChatRequest
{
    public Guid SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

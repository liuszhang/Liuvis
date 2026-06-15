namespace Liuvis.Core.Exceptions;

/// <summary>Thrown when NLU fails to parse user input.</summary>
public class NluParseException : BusinessException
{
    public NluParseException(string input, string reason = "Unknown reason")
        : base($"Failed to parse NLU intent for input '{input}': {reason}", 422)
    {
    }
}

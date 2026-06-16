namespace Liuvis.Core.Exceptions;

/// <summary>Base business exception for the Liuvis domain.</summary>
public class BusinessException : Exception
{
    public int ErrorCode { get; }

    public BusinessException(string message, int errorCode = 400) : base(message)
    {
        ErrorCode = errorCode;
    }

    public BusinessException(string message, Exception innerException, int errorCode = 400)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

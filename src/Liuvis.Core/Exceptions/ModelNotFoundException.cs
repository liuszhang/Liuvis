namespace Liuvis.Core.Exceptions;

/// <summary>Thrown when a requested model is not found.</summary>
public class ModelNotFoundException : BusinessException
{
    public ModelNotFoundException(Guid modelId)
        : base($"Model with ID '{modelId}' was not found.", 404)
    {
    }
}

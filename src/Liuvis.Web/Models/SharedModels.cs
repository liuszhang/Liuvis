namespace Liuvis.Web.Models;

/// <summary>View model for component tree display.</summary>
public record ComponentVm(Guid Id, string Name, string GeometryType, string? Color);

/// <summary>Arguments for model ready event.</summary>
public record ModelReadyArgs(string ModelUrl, List<ComponentVm> Components);

/// <summary>Arguments for property changed event.</summary>
public record PropertyChangedArgs(string? NewColor, string? NewMaterial);

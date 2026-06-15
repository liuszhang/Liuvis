namespace Liuvis.Infrastructure.ObjectStorage;

public class LocalStorageOptions
{
    public string Provider { get; set; } = "Local";
    public string BasePath { get; set; } = "./data/models";
}

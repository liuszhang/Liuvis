namespace Liuvis.Infrastructure.Configuration;

public class AppSettings
{
    public SessionSettings Session { get; set; } = new();
}

public class SessionSettings
{
    public int MaxContextRounds { get; set; } = 20;
    public int SessionTimeoutMinutes { get; set; } = 60;
}

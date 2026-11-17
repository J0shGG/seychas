namespace Lamp.Services;

public static class NotifyService
{
    public static Action<string, string>? Balloon;

    public static void Show(string title, string body) => Balloon?.Invoke(title, body);
}

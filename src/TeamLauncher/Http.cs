namespace TeamLauncher;

/// <summary>
/// Client HTTP partagé : une seule instance pour tout le launcher.
/// Évite l'allocation de sockets multiples et réduit la mémoire utilisée.
/// </summary>
public static class Http
{
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(10) };
}

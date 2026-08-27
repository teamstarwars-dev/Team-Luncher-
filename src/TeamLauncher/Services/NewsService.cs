using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Flux d'actualités affiché dans le launcher (page 📰 Actualités).
/// Format attendu (JSON, hébergé où tu veux — GitHub, ton site…) :
/// [
///   { "title": "Mise à jour 1.2", "date": "2026-08-24", "tag": "NOUVEAU", "text": "..." }
/// ]
/// L'URL se configure dans Paramètres → URL des actualités.
/// Le dernier flux reçu est mis en cache : la page marche même hors-ligne.
/// </summary>
public static class NewsService
{
    public sealed record NewsItem(string Title, string Date, string Tag, string Text);

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamLauncher", "news-cache.json");

    public static async Task<List<NewsItem>> GetAsync()
    {
        // Charger le changelog local (fichier persistant)
        var items = Changelog.GetAll()
            .Select(e => new NewsItem(e.Title, e.Date, e.Tag, e.Text))
            .ToList();

        string url = DataStore.Settings.NewsUrl.Trim();
        if (url.Length == 0) return items;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string json = await http.GetStringAsync(url);
            var remote = Parse(json);
            if (remote.Count > 0)
            {
                items.AddRange(remote);
                try { File.WriteAllText(CachePath, json); } catch { }
            }
        }
        catch
        {
            try
            {
                if (File.Exists(CachePath))
                    items.AddRange(Parse(await File.ReadAllTextAsync(CachePath)));
            }
            catch { }
        }

        return items;
    }

    private static List<NewsItem> Parse(string json)
    {
        var result = new List<NewsItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                result.Add(new NewsItem(
                    e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    e.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "",
                    e.TryGetProperty("tag", out var g) ? g.GetString() ?? "" : "",
                    e.TryGetProperty("text", out var x) ? x.GetString() ?? "" : ""));
            }
        }
        catch { }
        return result;
    }
}

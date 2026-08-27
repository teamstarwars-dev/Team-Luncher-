using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Système de changelog automatique.
/// Appelle Changelog.Add("Titre", "NOUVEAU", "Description…") depuis n'importe où
/// et l'entrée apparaît dans la page Actualités.
/// Les entrées sont persistées dans changelog.json (pas besoin de re-compiler).
/// </summary>
public static class Changelog
{
    public sealed record Entry(string Title, string Date, string Tag, string Text);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamLauncher", "changelog.json");

    private static List<Entry>? _cache;

    /// <summary>Ajoute une entrée au changelog (persistée, apparaît dans Actualités).</summary>
    public static void Add(string title, string tag, string text)
    {
        var entry = new Entry(title, DateTime.Now.ToString("yyyy-MM-dd"), tag, text);
        var entries = Load();
        entries.Insert(0, entry); // plus récent en premier
        Save(entries);
    }

    /// <summary>Récupère toutes les entrées (fichier + entrées hardcodées si vide).</summary>
    public static List<Entry> GetAll()
    {
        var entries = Load();

        // Si le fichier est vide, charger les entrées par défaut
        if (entries.Count == 0)
        {
            entries = DefaultEntries();
            Save(entries);
        }

        return entries;
    }

    private static List<Entry> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                _cache = JsonSerializer.Deserialize<List<Entry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                return _cache;
            }
        }
        catch { }
        _cache = new();
        return _cache;
    }

    private static void Save(List<Entry> entries)
    {
        _cache = entries;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static List<Entry> DefaultEntries() => new()
    {
        new("Sidebar compacte et minimaliste", "2026-08-27", "NOUVEAU",
            "• Sidebar réduite à 56px avec icônes emoji\n" +
            "• Profil au-dessus de Compte / Paramètres\n" +
            "• Tooltips au survol"),

        new("Page Instances en cartes", "2026-08-27", "NOUVEAU",
            "• Grille de cartes horizontales\n" +
            "• Clic → page détail, ✎ pour modifier\n" +
            "• Bouton Jouer sur chaque carte"),

        new("Page détail instance style CurseForge", "2026-08-27", "NOUVEAU",
            "• Bannière info complète\n" +
            "• Onglets Description / Mods / Mondes / Shaders / RP / Screenshots"),

        new("Partage avec code court", "2026-08-27", "NOUVEAU",
            "• Bouton 🔗 → zip ou code court\n" +
            "• Code type CurseForge (ex: ABCD-EFGH)"),

        new("Import CurseForge par URL", "2026-08-27", "NOUVEAU",
            "• URL, ID ou slug → installation auto du modpack"),

        new("Explorateur et Skins refaits", "2026-08-27", "FIX",
            "• Explorateur : style moderne en liste\n" +
            "• Skins : thumbnails arrondies, propre"),
    };
}

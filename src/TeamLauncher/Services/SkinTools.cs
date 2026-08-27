using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Outils skins : téléchargement du skin officiel d'un pseudo + découpe de la
/// tête (face + calque chapeau) pour les aperçus.
/// </summary>
public static class SkinTools
{
    public static string OfficialPath(string name) =>
        Path.Combine(DataStore.SkinsDir, name + ".png");

    /// <summary>Télécharge le skin officiel du pseudo s'il n'est pas déjà en local.</summary>
    public static async Task<bool> EnsureOfficialSkinAsync(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            Directory.CreateDirectory(DataStore.SkinsDir);
            string path = OfficialPath(name);
            if (File.Exists(path)) return true;

            using var http = new HttpClient();
            byte[] data = await http.GetByteArrayAsync(
                $"https://mc-heads.net/skin/{Uri.EscapeDataString(name)}");
            await File.WriteAllBytesAsync(path, data);
            return File.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>
    /// Tête du joueur agrandie (face 8×8 + calque chapeau), découpée depuis le
    /// fichier de skin local. Retourne null si le skin n'est pas disponible.
    /// </summary>
    public static Image? MakeHead(string name, int size = 40)
    {
        try
        {
            string path = OfficialPath(name);
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var tmp = Image.FromStream(fs);
            using var skin = new Bitmap(tmp);

            var head = new Bitmap(size, size);
            using var g = Graphics.FromImage(head);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Transparent);
            // face avant de la tête (8,8) puis calque chapeau (40,8)
            g.DrawImage(skin, new Rectangle(0, 0, size, size), new Rectangle(8, 8, 8, 8), GraphicsUnit.Pixel);
            g.DrawImage(skin, new Rectangle(0, 0, size, size), new Rectangle(40, 8, 8, 8), GraphicsUnit.Pixel);
            return head;
        }
        catch { return null; }
    }
}

using System.Net.NetworkInformation;

namespace TeamLauncher;

/// <summary>Diagnostic de santé du launcher au démarrage ou à la demande.</summary>
public static class HealthService
{
    public sealed record Check(string Name, bool Ok, string Detail);

    public static async Task<List<Check>> RunAllAsync()
    {
        var results = new List<Check>();

        // Java
        bool hasJava = await Task.Run(() => GameLauncher.FindJava(8) != null);
        results.Add(new Check("Java installé", hasJava,
            hasJava ? "Au moins un Java compatible détecté" :
                      "Aucun Java trouvé — le launcher en téléchargera un si besoin"));

        // Espace disque
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
            results.Add(new Check("Espace disque", freeGb > 2,
                $"{freeGb:0.#} Go libres sur {drive.Name}"));
        }
        catch { results.Add(new Check("Espace disque", true, "Non vérifiable")); }

        // Connexion Mojang
        bool net = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var resp = await http.GetAsync(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
            net = resp.IsSuccessStatusCode;
        }
        catch { }
        results.Add(new Check("Serveurs Mojang joignables", net,
            net ? "Téléchargements possibles" : "Vérifie ta connexion internet"));

        // Dossiers accessibles
        bool dirsOk;
        string dirDetail;
        try
        {
            Directory.CreateDirectory(DataStore.InstancesRoot);
            File.WriteAllText(Path.Combine(DataStore.InstancesRoot, ".test"), "x");
            File.Delete(Path.Combine(DataStore.InstancesRoot, ".test"));
            dirsOk = true; dirDetail = "Lecture/écriture OK";
        }
        catch (Exception ex) { dirsOk = false; dirDetail = ex.Message; }
        results.Add(new Check("Dossier des instances accessible", dirsOk, dirDetail));

        // Connexion Microsoft (informatif)
        results.Add(new Check("Connexion Microsoft", MsAuth.IsConfigured,
            "ID client intégré au launcher"));

        return results;
    }
}

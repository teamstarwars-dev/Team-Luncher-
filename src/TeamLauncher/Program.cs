using Microsoft.Win32;

namespace TeamLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // ---- Gestionnaire d'erreurs global ----
        // Sans ça, si une exception survient (registry, .NET manquant, ressource corrompue…),
        // l'exe se ferme SILENCIEUSEMENT sans aucun message.
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(
                "Une erreur inattendue s'est produite :\n\n" + e.Exception.Message +
                "\n\nDetails techniques :\n" + e.Exception,
                "Team Launcher — Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                try
                {
                    MessageBox.Show(
                        "Erreur critique :\n\n" + ex.Message,
                        "Team Launcher — Erreur critique", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        };

        try
        {
            // Enregistrer le protocole teamlauncher:// dans le registre Windows
            RegisterProtocol();

            // Hooks Velopack (installation/mise à jour) — sans effet si non packagé
            try { Velopack.VelopackApp.Build().Run(); } catch { }

            ApplicationConfiguration.Initialize();
            DataStore.Load();
            Theme.Reload();

            if (!DataStore.Settings.OnboardingDone || string.IsNullOrEmpty(DataStore.Settings.AccountMode))
            {
                using var dialog = new OnboardingDialog { SkipImport = true };
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;
            }

            string? deepLink = null;

            // Si lancé via un lien teamlauncher://, extraire l'URL
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].StartsWith("teamlauncher://", StringComparison.OrdinalIgnoreCase))
                deepLink = args[1];

            // Aussi via ArgumentReceived (si registeré comme protocole)
            if (string.IsNullOrEmpty(deepLink))
            {
                foreach (var a in args)
                {
                    if (a.StartsWith("teamlauncher://", StringComparison.OrdinalIgnoreCase))
                    {
                        deepLink = a;
                        break;
                    }
                }
            }

            var mainForm = new MainForm();

            if (!string.IsNullOrEmpty(deepLink))
            {
                mainForm.Shown += (_, _) => HandleDeepLink(mainForm, deepLink);
            }

            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Team Launcher n'a pas pu démarrer.\n\n" +
                "Erreur : " + ex.Message + "\n\n" +
                "Vérifie que :\n" +
                "• Le .NET 8 Desktop Runtime est installé (télécharge-le sur dotnet.microsoft.com)\n" +
                "• Le fichier n'est pas bloqué par Windows (clic droit → Propriétés → Débloquer)\n" +
                "• Tu as les droits d'écriture dans AppData\\Local\\TeamLauncher\n\n" +
                "Détails techniques :\n" + ex,
                "Team Launcher — Erreur au démarrage",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void HandleDeepLink(MainForm form, string url)
    {
        try
        {
            var uri = new Uri(url);
            string path = uri.AbsolutePath.TrimStart('/');

            if (path.StartsWith("import/", StringComparison.OrdinalIgnoreCase))
            {
                string code = path["import/".Length..];
                if (code.Length > 0)
                {
                    form.NavigateToInstances();
                    form.ImportSharedPackByCode(code);
                }
            }
        }
        catch { }
    }

    private static void RegisterProtocol()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (exePath.Length == 0) return;

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\teamlauncher");
            key.SetValue("", "URL:Team Launcher Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\"");

            using var cmdKey = key.CreateSubKey(@"shell\open\command");
            cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch { }
    }
}

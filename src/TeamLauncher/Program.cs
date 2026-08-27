using Microsoft.Win32;

namespace TeamLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
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
            using var dialog = new OnboardingDialog();
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
            // Quand Windows ouvre via protocole, l'URL arrive en args[1]
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
            // Traiter le lien après le chargement du formulaire
            mainForm.Shown += (_, _) => HandleDeepLink(mainForm, deepLink);
        }

        Application.Run(mainForm);
    }

    private static void HandleDeepLink(MainForm form, string url)
    {
        try
        {
            var uri = new Uri(url);
            string path = uri.AbsolutePath.TrimStart('/'); // "import/ABCDEF-GH"

            if (path.StartsWith("import/", StringComparison.OrdinalIgnoreCase))
            {
                string code = path["import/".Length..];
                if (code.Length > 0)
                {
                    // Naviguer vers la page instances et ouvrir l'import
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

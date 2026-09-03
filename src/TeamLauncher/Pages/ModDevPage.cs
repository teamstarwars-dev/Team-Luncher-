using System.Diagnostics;

namespace TeamLauncher;

/// <summary>
/// Page de développement de mods Minecraft.
/// Création de projets (Fabric/Forge/NeoForge/Bedrock), build, exécution, console.
/// </summary>
public class ModDevPage : UserControl, IRefreshable
{
    private readonly ComboBox loaderBox = new();
    private readonly ComboBox versionBox = new();
    private readonly TextBox projectNameBox = new();
    private readonly TextBox packageBox = new();
    private readonly TextBox projectPathBox = new();
    private readonly RichTextBox consoleBox = new();
    private readonly Button createBtn = new();
    private readonly Button buildBtn = new();
    private readonly Button runBtn = new();
    private readonly Button openBtn = new();
    private readonly TextBox cmdInput = new();
    private Process? currentProcess;

    private static readonly Dictionary<string, string[]> LoaderVersions = new()
    {
        ["Fabric"] = ["1.21.5", "1.21.4", "1.21.3", "1.21.2", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.2", "1.20.1", "1.20", "1.19.4", "1.19.3", "1.19.2", "1.18.2"],
        ["Forge"] = ["1.21.5", "1.21.4", "1.21.3", "1.21.2", "1.21.1", "1.20.6", "1.20.4", "1.20.2", "1.20.1", "1.19.4", "1.19.3", "1.19.2", "1.18.2", "1.16.5"],
        ["NeoForge"] = ["1.21.5", "1.21.4", "1.21.3", "1.21.2", "1.21.1", "1.20.6", "1.20.4", "1.20.2", "1.20.1"],
        ["Bedrock"] = ["1.21.50", "1.21.40", "1.21.30", "1.21.20", "1.21.10", "1.21.0", "1.20.80", "1.20.70", "1.20.60"]
    };

    public ModDevPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        BuildUI();
    }

    private void BuildUI()
    {
        var scroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = new Padding(0, 8, 0, 8)
        };

        // ---- Header ----
        scroll.Controls.Add(MakeHeader("🔨  Développement de Mods",
            "Crée, build et test tes mods Minecraft directement depuis le launcher."));

        // ---- Section: Nouveau projet ----
        scroll.Controls.Add(MakeSectionLabel("NOUVEAU PROJET"));

        var loaderRow = MakeRow();
        loaderRow.Controls.Add(MakeLabel("Loader :"));
        loaderBox.DropDownStyle = ComboBoxStyle.DropDownList;
        loaderBox.Width = 160;
        loaderBox.Items.AddRange(new object[] { "Fabric", "Forge", "NeoForge", "Bedrock" });
        loaderBox.SelectedIndex = 0;
        loaderBox.SelectedIndexChanged += (_, _) => UpdateVersions();
        loaderRow.Controls.Add(loaderBox);
        scroll.Controls.Add(loaderRow);

        var versionRow = MakeRow();
        versionRow.Controls.Add(MakeLabel("Version MC :"));
        versionBox.DropDownStyle = ComboBoxStyle.DropDownList;
        versionBox.Width = 160;
        UpdateVersions();
        versionRow.Controls.Add(versionBox);
        scroll.Controls.Add(versionRow);

        var nameRow = MakeRow();
        nameRow.Controls.Add(MakeLabel("Nom du mod :"));
        projectNameBox.Width = 200;
        projectNameBox.Text = "mymod";
        Theme.ApplyInput(projectNameBox);
        nameRow.Controls.Add(projectNameBox);
        scroll.Controls.Add(nameRow);

        var pkgRow = MakeRow();
        pkgRow.Controls.Add(MakeLabel("Package :"));
        packageBox.Width = 200;
        packageBox.Text = "com.example.mymod";
        Theme.ApplyInput(packageBox);
        pkgRow.Controls.Add(packageBox);
        scroll.Controls.Add(pkgRow);

        var pathRow = MakeRow();
        pathRow.Controls.Add(MakeLabel("Dossier :"));
        projectPathBox.Width = 280;
        projectPathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "mod-projects", "mymod");
        Theme.ApplyInput(projectPathBox);
        pathRow.Controls.Add(projectPathBox);
        var browseBtn = new Button { Text = "...", Width = 30, Height = 28 };
        Theme.Apply(browseBtn);
        browseBtn.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = projectPathBox.Text;
            if (fbd.ShowDialog(FindForm()) == DialogResult.OK)
                projectPathBox.Text = fbd.SelectedPath;
        };
        pathRow.Controls.Add(browseBtn);
        scroll.Controls.Add(pathRow);

        createBtn.Text = "🚀  Créer le projet";
        createBtn.Height = 36;
        createBtn.Width = 180;
        Theme.Apply(createBtn, primary: true);
        createBtn.Click += async (_, _) => await CreateProjectAsync();
        var btnRow = MakeRow();
        btnRow.Controls.Add(createBtn);
        scroll.Controls.Add(btnRow);

        // ---- Section: Build & Run ----
        scroll.Controls.Add(MakeSectionLabel("BUILD & RUN"));

        var actionRow = MakeRow();

        buildBtn.Text = "🔨  Build";
        buildBtn.Height = 32;
        buildBtn.Width = 100;
        Theme.Apply(buildBtn);
        buildBtn.Enabled = false;
        buildBtn.Click += async (_, _) => await BuildProjectAsync();
        actionRow.Controls.Add(buildBtn);

        runBtn.Text = "▶  Run Client";
        runBtn.Height = 32;
        runBtn.Width = 120;
        Theme.Apply(runBtn);
        runBtn.Enabled = false;
        runBtn.Click += async (_, _) => await RunProjectAsync("runClient");
        actionRow.Controls.Add(runBtn);

        var runServerBtn = new Button { Text = "🖥  Run Server", Height = 32, Width = 130 };
        Theme.Apply(runServerBtn);
        runServerBtn.Enabled = false;
        runServerBtn.Click += async (_, _) => await RunProjectAsync("runServer");
        actionRow.Controls.Add(runServerBtn);

        var stopBtn = new Button { Text = "⏹  Stop", Height = 32, Width = 80 };
        Theme.Apply(stopBtn);
        stopBtn.Click += (_, _) => { currentProcess?.Kill(); currentProcess = null; };
        actionRow.Controls.Add(stopBtn);

        openBtn.Text = "📂  Ouvrir le projet";
        openBtn.Height = 32;
        openBtn.Width = 140;
        Theme.Apply(openBtn);
        openBtn.Click += (_, _) =>
        {
            string dir = projectPathBox.Text.Trim();
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        };
        actionRow.Controls.Add(openBtn);
        scroll.Controls.Add(actionRow);

        // ---- Section: Console ----
        scroll.Controls.Add(MakeSectionLabel("CONSOLE"));

        consoleBox.Width = 900;
        consoleBox.Height = 250;
        consoleBox.BackColor = Color.FromArgb(30, 30, 30);
        consoleBox.ForeColor = Color.FromArgb(200, 200, 200);
        consoleBox.Font = new Font("Consolas", 9f);
        consoleBox.BorderStyle = BorderStyle.None;
        consoleBox.ReadOnly = true;
        consoleBox.WordWrap = false;
        scroll.Controls.Add(consoleBox);

        // Input pour écrire des commandes
        cmdInput.Width = 900;
        cmdInput.Height = 28;
        cmdInput.BackColor = Color.FromArgb(50, 50, 55);
        cmdInput.ForeColor = Color.FromArgb(200, 200, 200);
        cmdInput.Font = new Font("Consolas", 9.5f);
        cmdInput.BorderStyle = BorderStyle.FixedSingle;
        cmdInput.PlaceholderText = "Tape une commande ici (ex: gradlew build)...";
        cmdInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || string.IsNullOrWhiteSpace(cmdInput.Text)) return;
            e.SuppressKeyPress = true;
            string cmd = cmdInput.Text.Trim();
            cmdInput.Clear();
            LogConsole($"$ {cmd}");

            if (currentProcess != null && !currentProcess.HasExited)
            {
                try { currentProcess.StandardInput.WriteLine(cmd); }
                catch { LogConsole("❌ Impossible d'écrire dans le processus."); }
            }
            else
            {
                _ = RunCommandAsync(cmd);
            }
        };
        scroll.Controls.Add(cmdInput);

        // ---- Section: Ressources ----
        scroll.Controls.Add(MakeSectionLabel("RESSOURCES & DOCUMENTATION"));

        var links = new (string label, string url)[]
        {
            ("Fabric Docs", "https://fabricmc.net/wiki/"),
            ("Forge Docs", "https://docs.minecraftforge.net/"),
            ("NeoForge Docs", "https://docs.neoforged.net/"),
            ("Bedrock Creator Docs", "https://learn.microsoft.com/en-us/minecraft/creator/"),
            ("Blockbench (3D Models)", "https://blockbench.net/"),
            ("Minecraft Wiki", "https://minecraft.wiki/"),
            ("Modrinth (Publish)", "https://modrinth.com/"),
            ("CurseForge (Publish)", "https://www.curseforge.com/minecraft")
        };

        var linksRow = MakeRow();
        foreach (var (label, url) in links)
        {
            var linkBtn = new Button
            {
                Text = $"🔗  {label}",
                AutoSize = true,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0)
            };
            linkBtn.FlatAppearance.BorderSize = 0;
            linkBtn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            };
            linksRow.Controls.Add(linkBtn);
        }
        scroll.Controls.Add(linksRow);

        Controls.Add(scroll);
    }

    private void UpdateVersions()
    {
        versionBox.Items.Clear();
        string loader = loaderBox.SelectedItem?.ToString() ?? "Fabric";
        if (LoaderVersions.TryGetValue(loader, out var versions))
            versionBox.Items.AddRange(versions);
        if (versionBox.Items.Count > 0)
            versionBox.SelectedIndex = 0;
    }

    private async Task CreateProjectAsync()
    {
        string loader = loaderBox.SelectedItem?.ToString() ?? "Fabric";
        string version = versionBox.SelectedItem?.ToString() ?? "1.21.4";
        string name = projectNameBox.Text.Trim();
        string pkg = packageBox.Text.Trim();
        string dir = projectPathBox.Text.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dir))
        {
            MessageBox.Show("Remplis le nom du mod et le dossier.", "Team Launcher");
            return;
        }

        createBtn.Enabled = false;
        createBtn.Text = "⏳ Création...";
        LogConsole($"=== Création d'un projet {loader} {version} ===");
        LogConsole($"Nom: {name} | Package: {pkg}");
        LogConsole($"Dossier: {dir}");

        try
        {
            Directory.CreateDirectory(dir);

            if (loader == "Bedrock")
                await CreateBedrockProjectAsync(dir, name, version);
            else
                await CreateJavaModProjectAsync(dir, name, pkg, loader, version);

            LogConsole("✅ Projet créé avec succès !");
            buildBtn.Enabled = true;
            runBtn.Enabled = true;
        }
        catch (Exception ex)
        {
            LogConsole($"❌ Erreur: {ex.Message}");
        }
        finally
        {
            createBtn.Enabled = true;
            createBtn.Text = "🚀  Créer le projet";
        }
    }

    private async Task CreateJavaModProjectAsync(string dir, string name, string pkg, string loader, string mcVersion)
    {
        string pkgPath = pkg.Replace('.', '/');

        // build.gradle
        string gradleContent = loader switch
        {
            "Fabric" => $@"plugins {{
    id 'fabric-loom' version '1.9-SNAPSHOT'
    id 'maven-publish'
}}

version = '1.0.0'
group = '{pkg}'

base {{
    archivesName = '{name}'
}}

repositories {{
    mavenCentral()
}}

dependencies {{
    minecraft ""com.mojang:minecraft:{mcVersion}""
    mappings ""net.fabricmc:yarn:{mcVersion}+build.1:v2""
    modImplementation ""net.fabricmc.fabric-api:fabric-api:{mcVersion}+""
}}

processResources {{
    inputs.property ""version"", project.version
    filesMatching(""fabric.mod.json"") {{
        expand ""version"": project.version
    }}
}}

java {{
    sourceCompatibility = JavaVersion.VERSION_21
    targetCompatibility = JavaVersion.VERSION_21
}}

tasks.withType(JavaCompile).configureEach {{
    options.encoding = UTF8
}}",
            "Forge" or "NeoForge" => $@"plugins {{
    id 'net.minecraftforge.gradle' version '[6.0.16,6.2)'
}}

version = '1.0.0'
group = '{pkg}'

base {{
    archivesName = '{name}'
}}

minecraft {{
    mappings channel: 'official', version: '{mcVersion}'
}}

dependencies {{
    minecraft ""com.mojang:minecraft:{mcVersion}""
    minecraft ""net.minecraftforge:forge:{mcVersion}-latest""
}}

java {{
    sourceCompatibility = JavaVersion.VERSION_21
    targetCompatibility = JavaVersion.VERSION_21
}}

tasks.withType(JavaCompile).configureEach {{
    options.encoding = UTF8
}}",
            _ => $"// {loader} project for {mcVersion}"
        };

        await File.WriteAllTextAsync(Path.Combine(dir, "build.gradle"), gradleContent);

        // gradle.properties
        await File.WriteAllTextAsync(Path.Combine(dir, "gradle.properties"), $@"
org.gradle.jvmargs=-Xmx2G
minecraft_version={mcVersion}
mod_name={name}
mod_id={name.ToLower().Replace(" ", "_")}
mod_version=1.0.0
");

        // settings.gradle
        await File.WriteAllTextAsync(Path.Combine(dir, "settings.gradle"), $@"rootProject.name = '{name}'");

        // Java source
        string srcDir = Path.Combine(dir, "src", "main", "java", pkgPath);
        Directory.CreateDirectory(srcDir);

        string modId = name.ToLower().Replace(" ", "_");
        string className = name.Replace(" ", "");

        string modContent = loader == "Fabric"
            ? "package " + pkg + ";\n\nimport net.fabricmc.api.ModInitializer;\nimport org.slf4j.LoggerFactory;\n\npublic class " + className + " implements ModInitializer {\n    public static final String MOD_ID = \"" + modId + "\";\n    public static final var LOGGER = LoggerFactory.getLogger(MOD_ID);\n\n    @Override\n    public void onInitialize() {\n        LOGGER.info(\"" + name + " loaded!\", MOD_ID);\n    }\n}"
            : "package " + pkg + ";\n\nimport net.minecraftforge.fml.common.Mod;\nimport org.slf4j.Logger;\nimport com.mojang.logging.LogUtils;\n\n@Mod(\"" + modId + "\")\npublic class " + className + " {\n    public static final String MOD_ID = \"" + modId + "\";\n    private static final Logger LOGGER = LogUtils.getLogger();\n\n    public " + className + "() {\n        LOGGER.info(\"" + name + " loaded!\", MOD_ID);\n    }\n}";

        await File.WriteAllTextAsync(Path.Combine(srcDir, className + ".java"), modContent);

        // Resources
        string resDir = Path.Combine(dir, "src", "main", "resources");
        Directory.CreateDirectory(resDir);

        if (loader == "Fabric")
        {
            await File.WriteAllTextAsync(Path.Combine(resDir, "fabric.mod.json"), $@"{{
  ""schemaVersion"": 1,
  ""id"": ""{modId}"",
  ""version"": ""1.0.0"",
  ""name"": ""{name}"",
  ""environment"": ""*"",
  ""entrypoints"": {{
    ""main"": [""{pkg}.{className}""]
  }},
  ""depends"": {{
    ""fabricloader"": "">=0.15.0"",
    ""fabric"": """",
    ""minecraft"": ""{mcVersion}"",
    ""java"": "">=21""
  }}
}}");
        }

        LogConsole($"Fichiers créés: build.gradle, settings.gradle, gradle.properties");
        LogConsole($"Source: src/main/java/{pkgPath}/{className}.java");
    }

    private async Task CreateBedrockProjectAsync(string dir, string name, string version)
    {
        string ns = name.ToLower().Replace(" ", "_");

        // behavior pack
        string bpDir = Path.Combine(dir, "BP");
        Directory.CreateDirectory(Path.Combine(bpDir, "entities"));
        Directory.CreateDirectory(Path.Combine(bpDir, "blocks"));
        Directory.CreateDirectory(Path.Combine(bpDir, "items"));
        Directory.CreateDirectory(Path.Combine(bpDir, "scripts"));

        await File.WriteAllTextAsync(Path.Combine(bpDir, "manifest.json"), $@"{{
  ""format_version"": 2,
  ""header"": {{
    ""name"": ""{name}"",
    ""description"": ""Custom add-on"",
    ""uuid"": ""{Guid.NewGuid()}"",
    ""version"": [1, 0, 0],
    ""min_engine_version"": [1, 21, 0]
  }},
  ""modules"": [
    {{
      ""type"": ""data"",
      ""uuid"": ""{Guid.NewGuid()}"",
      ""version"": [1, 0, 0]
    }}
  ]
}}");

        // resource pack
        string rpDir = Path.Combine(dir, "RP");
        Directory.CreateDirectory(Path.Combine(rpDir, "textures"));
        Directory.CreateDirectory(Path.Combine(rpDir, "models"));

        await File.WriteAllTextAsync(Path.Combine(rpDir, "manifest.json"), $@"{{
  ""format_version"": 2,
  ""header"": {{
    ""name"": ""{name} Resources"",
    ""description"": ""Resource pack"",
    ""uuid"": ""{Guid.NewGuid()}"",
    ""version"": [1, 0, 0],
    ""min_engine_version"": [1, 21, 0]
  }},
  ""modules"": [
    {{
      ""type"": ""resources"",
      ""uuid"": ""{Guid.NewGuid()}"",
      ""version"": [1, 0, 0]
    }}
  ]
}}");

        LogConsole($"Dossier Bedrock créé: BP/ (behavior), RP/ (resources)");
        LogConsole($"Utilise Blockbench pour créer les modèles 3D");
    }

    private async Task BuildProjectAsync()
    {
        string dir = projectPathBox.Text.Trim();
        string gradlew = Path.Combine(dir, "gradlew.bat");
        if (!File.Exists(gradlew))
        {
            LogConsole("❌ gradlew.bat non trouvé. Crée le projet d'abord.");
            return;
        }

        buildBtn.Enabled = false;
        buildBtn.Text = "⏳ Build...";
        LogConsole("=== BUILD ===");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gradlew,
                Arguments = "build",
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            currentProcess = Process.Start(psi);
            if (currentProcess == null) return;

            _ = Task.Run(async () =>
            {
                while (!currentProcess.StandardOutput.EndOfStream)
                {
                    var line = await currentProcess.StandardOutput.ReadLineAsync();
                    if (line != null) BeginInvoke(() => LogConsole(line));
                }
            });
            _ = Task.Run(async () =>
            {
                while (!currentProcess.StandardError.EndOfStream)
                {
                    var line = await currentProcess.StandardError.ReadLineAsync();
                    if (line != null) BeginInvoke(() => LogConsole(line));
                }
            });

            await currentProcess.WaitForExitAsync();
            LogConsole(currentProcess.ExitCode == 0 ? "✅ Build réussi !" : $"❌ Build échoué (code {currentProcess.ExitCode})");
        }
        catch (Exception ex)
        {
            LogConsole($"❌ Erreur: {ex.Message}");
        }
        finally
        {
            currentProcess = null;
            buildBtn.Enabled = true;
            buildBtn.Text = "🔨  Build";
        }
    }

    private async Task RunProjectAsync(string task)
    {
        string dir = projectPathBox.Text.Trim();
        string gradlew = Path.Combine(dir, "gradlew.bat");
        if (!File.Exists(gradlew))
        {
            LogConsole("❌ gradlew.bat non trouvé.");
            return;
        }

        runBtn.Enabled = false;
        LogConsole($"=== {task.ToUpper()} ===");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gradlew,
                Arguments = task,
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            currentProcess = Process.Start(psi);
            if (currentProcess == null) return;

            _ = Task.Run(async () =>
            {
                while (!currentProcess.StandardOutput.EndOfStream)
                {
                    var line = await currentProcess.StandardOutput.ReadLineAsync();
                    if (line != null) BeginInvoke(() => LogConsole(line));
                }
            });

            await currentProcess.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            LogConsole($"❌ Erreur: {ex.Message}");
        }
        finally
        {
            currentProcess = null;
            runBtn.Enabled = true;
        }
    }

    private void LogConsole(string text)
    {
        Color color = text.Contains("ERROR") || text.Contains("❌") ? Color.FromArgb(255, 80, 80)
            : text.Contains("WARN") ? Color.FromArgb(255, 200, 60)
            : text.Contains("✅") ? Color.FromArgb(80, 200, 80)
            : Color.FromArgb(200, 200, 200);

        consoleBox.SelectionStart = consoleBox.TextLength;
        consoleBox.SelectionLength = 0;
        consoleBox.SelectionColor = color;
        consoleBox.AppendText(text + "\n");
        consoleBox.SelectionStart = consoleBox.TextLength;
    }

    private async Task RunCommandAsync(string cmd)
    {
        try
        {
            var parts = cmd.Split(' ', 2);
            var psi = new ProcessStartInfo
            {
                FileName = parts[0],
                Arguments = parts.Length > 1 ? parts[1] : "",
                WorkingDirectory = projectPathBox.Text.Trim(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            if (proc == null) return;

            _ = Task.Run(async () =>
            {
                while (!proc.StandardOutput.EndOfStream)
                {
                    var line = await proc.StandardOutput.ReadLineAsync();
                    if (line != null) BeginInvoke(() => LogConsole(line));
                }
            });
            _ = Task.Run(async () =>
            {
                while (!proc.StandardError.EndOfStream)
                {
                    var line = await proc.StandardError.ReadLineAsync();
                    if (line != null) BeginInvoke(() => LogConsole(line));
                }
            });

            await proc.WaitForExitAsync();
            LogConsole(proc.ExitCode == 0 ? "✅ Commande terminée" : $"❌ Code: {proc.ExitCode}");
        }
        catch (Exception ex)
        {
            LogConsole($"❌ {ex.Message}");
        }
    }

    public void RefreshData() { }

    // ---- helpers UI ----

    private static Panel MakeHeader(string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(20, 12, 20, 8) };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, 8)
        };
        var subLabel = new Label
        {
            Text = subtitle,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.TextDim,
            AutoSize = true,
            Location = new Point(20, 38)
        };
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subLabel);
        return panel;
    }

    private static Panel MakeSection(string title)
    {
        var section = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(20, 4, 20, 12)
        };
        var label = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(20, 8)
        };
        section.Controls.Add(label);
        return section;
    }

    private static Panel MakeRow()
    {
        return new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(20, 4, 0, 4),
            AutoSize = false
        };
    }

    private static Label MakeSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Margin = new Padding(20, 12, 0, 4)
        };
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.Text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 6, 8, 0)
        };
    }
}

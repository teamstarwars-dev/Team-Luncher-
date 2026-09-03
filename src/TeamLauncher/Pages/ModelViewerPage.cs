using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Page de modélisation 3D et visualisation de modèles Minecraft.
/// Rendu isométrique GDI+ + outils externes (Blockbench, etc.)
/// </summary>
public class ModelViewerPage : UserControl, IRefreshable
{
    private readonly Panel viewportPanel = new();
    private readonly Panel toolsPanel = new();
    private readonly Label infoLabel = new();
    private readonly Button openBlockbenchBtn = new();
    private readonly Button loadModelBtn = new();
    private readonly Button resetViewBtn = new();

    private float rotX = 30f, rotY = 45f;
    private float zoom = 1f;
    private Point lastMouse;
    private bool dragging;
    private string? loadedModelPath;

    // Simple cube model for preview
    private readonly List<(Point3D pos, Color color, Size3D size)> cubes = new();

    public ModelViewerPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        BuildUI();
        LoadDefaultModel();
    }

    private void BuildUI()
    {
        // ---- Left: Viewport ----
        viewportPanel.Dock = DockStyle.Fill;
        viewportPanel.BackColor = Color.FromArgb(40, 40, 45);
        viewportPanel.Paint += Viewport_Paint;
        viewportPanel.MouseDown += (_, e) => { dragging = true; lastMouse = e.Location; };
        viewportPanel.MouseUp += (_, _) => dragging = false;
        viewportPanel.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            rotY += (e.X - lastMouse.X) * 0.5f;
            rotX += (e.Y - lastMouse.Y) * 0.5f;
            rotX = Math.Clamp(rotX, -90f, 90f);
            lastMouse = e.Location;
            viewportPanel.Invalidate();
        };
        viewportPanel.MouseWheel += (_, e) =>
        {
            zoom += e.Delta > 0 ? 0.1f : -0.1f;
            zoom = Math.Clamp(zoom, 0.2f, 5f);
            viewportPanel.Invalidate();
        };
        viewportPanel.Resize += (_, _) => viewportPanel.Invalidate();

        // ---- Right: Tools ----
        toolsPanel.Dock = DockStyle.Right;
        toolsPanel.Width = 240;
        toolsPanel.BackColor = Theme.Panel;
        toolsPanel.Padding = new Padding(12);

        var titleLabel = new Label
        {
            Text = "🎨  Modélisation 3D",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(12, 12)
        };
        toolsPanel.Controls.Add(titleLabel);

        // ---- Outils externes ----
        var toolsHeader = new Label
        {
            Text = "OUTILS",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(12, 48)
        };
        toolsPanel.Controls.Add(toolsHeader);

        int y = 68;
        var tools = new (string label, string url, string desc)[]
        {
            ("Blockbench", "https://blockbench.net/", "Éditeur 3D pour Minecraft"),
            ("Mine-imator", "https://www.mineimator.com/", "Animations Minecraft"),
            ("Cinema 4D", "https://www.maxon.net/", "Rendu pro (gratuit étudiants)"),
            ("Blender", "https://www.blender.org/", "3D gratuit et open source"),
            ("Pixel Studio", "https://editor.paradulse.net/", "Éditeur pixel art 3D")
        };

        foreach (var (label, url, desc) in tools)
        {
            var btn = new Button
            {
                Text = $"🔗  {label}",
                Size = new Size(210, 28),
                Location = new Point(12, y),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            };
            toolsPanel.Controls.Add(btn);

            var descLabel = new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Theme.TextDim,
                AutoSize = true,
                Location = new Point(18, y + 28)
            };
            toolsPanel.Controls.Add(descLabel);
            y += 52;
        }

        // ---- Actions viewport ----
        y += 10;
        var viewHeader = new Label
        {
            Text = "VIEWPORT",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(12, y)
        };
        toolsPanel.Controls.Add(viewHeader);
        y += 20;

        openBlockbenchBtn.Text = "🔧  Ouvrir Blockbench";
        openBlockbenchBtn.Size = new Size(210, 30);
        openBlockbenchBtn.Location = new Point(12, y);
        Theme.Apply(openBlockbenchBtn, primary: true);
        openBlockbenchBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://blockbench.net/") { UseShellExecute = true }); } catch { }
        };
        toolsPanel.Controls.Add(openBlockbenchBtn);
        y += 38;

        loadModelBtn.Text = "📂  Charger un modèle";
        loadModelBtn.Size = new Size(210, 30);
        loadModelBtn.Location = new Point(12, y);
        Theme.Apply(loadModelBtn);
        loadModelBtn.Click += LoadModel_Click;
        toolsPanel.Controls.Add(loadModelBtn);
        y += 38;

        resetViewBtn.Text = "🔄  Réinitialiser la vue";
        resetViewBtn.Size = new Size(210, 30);
        resetViewBtn.Location = new Point(12, y);
        Theme.Apply(resetViewBtn);
        resetViewBtn.Click += (_, _) => { rotX = 30; rotY = 45; zoom = 1; viewportPanel.Invalidate(); };
        toolsPanel.Controls.Add(resetViewBtn);
        y += 48;

        // ---- Info ----
        infoLabel.Text = "Souris: drag = tourner, molette = zoom";
        infoLabel.Font = new Font("Segoe UI", 8f);
        infoLabel.ForeColor = Theme.TextDim;
        infoLabel.AutoSize = true;
        infoLabel.Location = new Point(12, y);
        toolsPanel.Controls.Add(infoLabel);

        Controls.Add(viewportPanel);
        Controls.Add(toolsPanel);
    }

    private void LoadDefaultModel()
    {
        // Simple Minecraft-style character preview
        Color skin = Color.FromArgb(200, 160, 120);
        Color shirt = Color.FromArgb(50, 100, 200);
        Color pants = Color.FromArgb(50, 50, 150);
        Color shoes = Color.FromArgb(60, 60, 60);
        Color eyes = Color.White;
        Color pupils = Color.Black;

        // Head
        cubes.Add((new Point3D(0, 6, 0), skin, new Size3D(4, 4, 4)));
        // Eyes
        cubes.Add((new Point3D(-1, 7, -2), eyes, new Size3D(1, 1, 0)));
        cubes.Add((new Point3D(1, 7, -2), eyes, new Size3D(1, 1, 0)));
        cubes.Add((new Point3D(-1, 7, -2.1f), pupils, new Size3D(0.5f, 0.5f, 0)));
        cubes.Add((new Point3D(1, 7, -2.1f), pupils, new Size3D(0.5f, 0.5f, 0)));
        // Body
        cubes.Add((new Point3D(0, 2, 0), shirt, new Size3D(4, 4, 2)));
        // Arms
        cubes.Add((new Point3D(-3, 2, 0), skin, new Size3D(2, 4, 2)));
        cubes.Add((new Point3D(3, 2, 0), skin, new Size3D(2, 4, 2)));
        // Legs
        cubes.Add((new Point3D(-1, -2, 0), pants, new Size3D(2, 4, 2)));
        cubes.Add((new Point3D(1, -2, 0), pants, new Size3D(2, 4, 2)));
        // Shoes
        cubes.Add((new Point3D(-1, -4, 0), shoes, new Size3D(2, 1, 3)));
        cubes.Add((new Point3D(1, -4, 0), shoes, new Size3D(2, 1, 3)));
    }

    private void LoadModel_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Modèles 3D|*.obj;*.json;*.bbmodel;*.geo.json|Tous les fichiers|*.*"
        };
        if (ofd.ShowDialog(FindForm()) == DialogResult.OK)
        {
            loadedModelPath = ofd.FileName;
            infoLabel.Text = $"Modèle: {Path.GetFileName(loadedModelPath)}";
            // For now, just show a default cube model
            cubes.Clear();
            LoadDefaultModel();
            viewportPanel.Invalidate();
        }
    }

    private void Viewport_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(40, 40, 45));

        int cx = viewportPanel.Width / 2;
        int cy = viewportPanel.Height / 2;
        float scale = 12f * zoom;

        float radX = rotX * MathF.PI / 180f;
        float radY = rotY * MathF.PI / 180f;

        float cosY = MathF.Cos(radY), sinY = MathF.Sin(radY);
        float cosX = MathF.Cos(radX), sinX = MathF.Sin(radX);

        // Sort cubes by depth for painter's algorithm
        var sorted = cubes
            .Select(c => new
            {
                c.pos, c.color, c.size,
                depth = c.pos.X * sinY * cosX + c.pos.Y * sinX + c.pos.Z * cosY * cosX
            })
            .OrderByDescending(c => c.depth)
            .ToList();

        foreach (var cube in sorted)
        {
            // Project 3D to 2D (isometric-ish)
            float x = cube.pos.X, y = cube.pos.Y, z = cube.pos.Z;

            // Rotate Y
            float rx = x * cosY - z * sinY;
            float rz = x * sinY + z * cosY;
            // Rotate X
            float ry = y * cosX - rz * sinX;
            rz = y * sinX + rz * cosX;

            float sx = cx + rx * scale;
            float sy = cy - ry * scale;

            float w = cube.size.Width * scale * 0.5f;
            float h = cube.size.Height * scale * 0.5f;

            using var brush = new SolidBrush(cube.color);
            using var pen = new Pen(ControlPaint.Dark(cube.color, 0.2f), 1);

            // Draw cube face (simplified)
            var rect = new RectangleF(sx - w, sy - h, w * 2, h * 2);
            g.FillRectangle(brush, rect);
            g.DrawRectangle(pen, rect);

            // Highlight top edge
            using var highlight = new Pen(ControlPaint.Light(cube.color, 0.3f), 2);
            g.DrawLine(highlight, sx - w, sy - h, sx + w, sy - h);
        }

        // Grid
        using var gridPen = new Pen(Color.FromArgb(30, 30, 35), 1);
        for (int i = -10; i <= 10; i++)
        {
            float gx1 = cx + i * scale * 0.5f;
            float gz1 = cy + 0;
            g.DrawLine(gridPen, gx1, cy - 200, gx1, cy + 200);
        }
    }

    public void RefreshData() { }

    // ---- helpers ----

    private record struct Point3D(float X, float Y, float Z);
    private record struct Size3D(float Width, float Height, float Depth);
}

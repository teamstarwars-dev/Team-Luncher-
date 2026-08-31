using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Visualiseur 3D isométrique d'un monde Minecraft.
/// Lit les chunks, affiche les blocs en vue isométrique
/// avec rotation, zoom et navigation. Couleurs basées sur le type de bloc.
/// </summary>
public class WorldViewer3D : Control
{
    private readonly Dictionary<(int X, int Z), int> _heightmap = new();
    private readonly Dictionary<(int X, int Z), string> _topBlocks = new();
    private float _zoom = 1f;
    private float _offsetX, _offsetY;
    private float _rotation = 0.785f;
    private bool _dragging;
    private Point _lastMouse;
    private string? _worldPath;
    private bool _loaded;

    private static readonly Dictionary<string, Color> BlockColors = new()
    {
        ["minecraft:grass_block"] = Color.FromArgb(90, 150, 50),
        ["minecraft:stone"] = Color.FromArgb(125, 125, 125),
        ["minecraft:cobblestone"] = Color.FromArgb(100, 100, 100),
        ["minecraft:dirt"] = Color.FromArgb(134, 96, 67),
        ["minecraft:sand"] = Color.FromArgb(219, 211, 160),
        ["minecraft:water"] = Color.FromArgb(50, 100, 200),
        ["minecraft:lava"] = Color.FromArgb(200, 80, 0),
        ["minecraft:oak_planks"] = Color.FromArgb(160, 130, 70),
        ["minecraft:spruce_planks"] = Color.FromArgb(110, 80, 50),
        ["minecraft:birch_planks"] = Color.FromArgb(190, 170, 130),
        ["minecraft:bricks"] = Color.FromArgb(150, 70, 60),
        ["minecraft:glass"] = Color.FromArgb(180, 210, 230),
        ["minecraft:iron_block"] = Color.FromArgb(200, 200, 210),
        ["minecraft:diamond_block"] = Color.FromArgb(80, 200, 210),
        ["minecraft:gold_block"] = Color.FromArgb(230, 200, 50),
        ["minecraft:coal_ore"] = Color.FromArgb(80, 80, 80),
        ["minecraft:iron_ore"] = Color.FromArgb(150, 130, 120),
        ["minecraft:gold_ore"] = Color.FromArgb(180, 160, 80),
        ["minecraft:diamond_ore"] = Color.FromArgb(80, 180, 190),
        ["minecraft:leaves"] = Color.FromArgb(40, 120, 30),
        ["minecraft:oak_log"] = Color.FromArgb(100, 80, 50),
        ["minecraft:glass_pane"] = Color.FromArgb(180, 210, 230),
        ["minecraft:stone_bricks"] = Color.FromArgb(120, 120, 120),
        ["minecraft:gravel"] = Color.FromArgb(130, 125, 120),
        ["minecraft:snow_block"] = Color.FromArgb(240, 240, 250),
        ["minecraft:obsidian"] = Color.FromArgb(20, 10, 30),
    };

    public event Action<string>? OnStatusChanged;

    public WorldViewer3D()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(18, 20, 24);
        MinimumSize = new Size(200, 200);
    }

    public void LoadWorldClear()
    {
        _worldPath = null;
        _heightmap.Clear();
        _topBlocks.Clear();
        _loaded = false;
        Invalidate();
    }

    public async Task LoadWorldAsync(string worldPath, Action<string>? progress = null)
    {
        _worldPath = worldPath;
        _heightmap.Clear();
        _topBlocks.Clear();
        _loaded = false;

        if (string.IsNullOrEmpty(worldPath) || !Directory.Exists(worldPath))
        {
            Invalidate();
            return;
        }

        try
        {
            var result = await Task.Run(() =>
            {
                var heightmap = new Dictionary<(int X, int Z), int>();
                var topBlocks = new Dictionary<(int X, int Z), string>();

                string regionDir = Path.Combine(worldPath, "region");
                if (!Directory.Exists(regionDir)) return (heightmap, topBlocks);

                var files = Directory.GetFiles(regionDir, "*.mca");
                int fileCount = files.Length;

                for (int fi = 0; fi < fileCount; fi++)
                {
                    var file = files[fi];
                    var m = System.Text.RegularExpressions.Regex.Match(
                        Path.GetFileName(file), @"r\.(-?\d+)\.(-?\d+)\.mca$");
                    if (!m.Success) continue;
                    int rx = int.Parse(m.Groups[1].Value);
                    int rz = int.Parse(m.Groups[2].Value);

                    if (fi % 5 == 0)
                        progress?.Invoke($"Lecture région {fi + 1}/{fileCount}…");

                    for (int lx = 0; lx < 32; lx++)
                    {
                        for (int lz = 0; lz < 32; lz++)
                        {
                            int cx = rx * 32 + lx;
                            int cz = rz * 32 + lz;

                            try
                            {
                                var chunk = ChunkReader.ReadChunk(file, cx, cz);
                                if (chunk == null) continue;

                                var blocks = ChunkReader.GetBlocks(chunk);
                                for (int x = 0; x < 16; x++)
                                {
                                    for (int z = 0; z < 16; z++)
                                    {
                                        int worldX = cx * 16 + x;
                                        int worldZ = cz * 16 + z;
                                        int maxY = -64;
                                        string topBlock = "minecraft:air";

                                        for (int y = 319; y >= -64; y--)
                                        {
                                            if (blocks.TryGetValue((x, y, z), out var name) &&
                                                name != "minecraft:air")
                                            {
                                                maxY = y;
                                                topBlock = name;
                                                break;
                                            }
                                        }

                                        if (maxY > -64)
                                        {
                                            heightmap[(worldX, worldZ)] = maxY;
                                            topBlocks[(worldX, worldZ)] = topBlock;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                return (heightmap, topBlocks);
            });

            foreach (var kv in result.heightmap) _heightmap[kv.Key] = kv.Value;
            foreach (var kv in result.topBlocks) _topBlocks[kv.Key] = kv.Value;
            _loaded = true;

            if (_heightmap.Count > 0) CenterView();
            OnStatusChanged?.Invoke($"Monde chargé : {_heightmap.Count:N0} colonnes.");
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"Erreur: {ex.Message}");
        }

        Invalidate();
    }

    private void CenterView()
    {
        if (_heightmap.Count == 0) return;
        int minX = _heightmap.Keys.Min(k => k.X);
        int maxX = _heightmap.Keys.Max(k => k.X);
        int minZ = _heightmap.Keys.Min(k => k.Z);
        int maxZ = _heightmap.Keys.Max(k => k.Z);

        float worldCX = (minX + maxX) / 2f;
        float worldCZ = (minZ + maxZ) / 2f;

        _offsetX = Width / 2f - worldCX * _zoom * 1.4f;
        _offsetY = Height / 3f;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);

        if (!_loaded || _heightmap.Count == 0)
        {
            using var f = new Font("Segoe UI", 11f);
            string msg = _worldPath == null ? "Charge un monde pour le visualiser." : "Chargement…";
            var sz = TextRenderer.MeasureText(msg, f);
            TextRenderer.DrawText(g, msg, f,
                new Point((Width - sz.Width) / 2, (Height - sz.Height) / 2), Theme.TextDim);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float cos = MathF.Cos(_rotation);
        float sin = MathF.Sin(_rotation);

        var columns = new List<(int X, int Z, int H, string BlockName)>();
        foreach (var k in _heightmap.Keys)
        {
            _topBlocks.TryGetValue(k, out string? blk);
            columns.Add((k.X, k.Z, _heightmap[k], blk ?? "minecraft:stone"));
        }
        columns = columns
            .OrderBy(c => -c.X * sin + c.Z * cos)
            .ThenBy(c => c.H)
            .ToList();

        foreach (var col in columns)
        {
            int x = col.X, z = col.Z, y = col.H;
            string block = col.BlockName;
            float isoX = (x - z) * _zoom * 0.7f;
            float isoY = (x + z) * _zoom * 0.35f - y * _zoom * 0.8f;

            float px = _offsetX + isoX;
            float py = _offsetY + isoY;

            if (px < -50 || px > Width + 50 || py < -50 || py > Height + 50)
                continue;

            Color color = GetBlockColor(block);

            float heightFactor = Math.Clamp((y + 64) / 384f, 0.3f, 1f);
            int r = (int)(color.R * heightFactor);
            int gg = (int)(color.G * heightFactor);
            int b = (int)(color.B * heightFactor);
            var shaded = Color.FromArgb(255, Math.Clamp(r, 0, 255),
                                             Math.Clamp(gg, 0, 255),
                                             Math.Clamp(b, 0, 255));

            float sz = Math.Max(_zoom * 1.4f, 2f);

            var top = new PointF(px, py - sz * 0.4f);
            var left = new PointF(px - sz * 0.7f, py);
            var bottom = new PointF(px, py + sz * 0.4f);
            var right = new PointF(px + sz * 0.7f, py);

            using var brush = new SolidBrush(shaded);
            g.FillPolygon(brush, new[] { top, left, bottom, right });

            var rightDark = ControlPaint.Dark(shaded, 0.2f);
            using var rightBrush = new SolidBrush(rightDark);
            g.FillPolygon(rightBrush, new[] { left, bottom,
                new PointF(px, py + sz * 0.8f), new PointF(px - sz * 0.7f, py + sz * 0.4f) });

            var leftDark = ControlPaint.Dark(shaded, 0.35f);
            using var leftBrush = new SolidBrush(leftDark);
            g.FillPolygon(leftBrush, new[] { bottom, right,
                new PointF(px, py + sz * 0.8f), new PointF(px + sz * 0.7f, py + sz * 0.4f) });
        }

        using var infoFont = new Font("Consolas", 9f);
        TextRenderer.DrawText(g,
            $"Zoom: {_zoom:F1}x | Blocs: {_heightmap.Count:N0} | Rot: {(int)(_rotation * 180 / MathF.PI)}°",
            infoFont, new Point(8, 8), Color.FromArgb(150, 200, 200, 200));
    }

    private static Color GetBlockColor(string block)
    {
        if (BlockColors.TryGetValue(block, out var c)) return c;

        int hash = block.GetHashCode();
        int r = 80 + (hash & 0x7F);
        int gg = 80 + ((hash >> 8) & 0x7F);
        int b = 80 + ((hash >> 16) & 0x7F);
        return Color.FromArgb(255, Math.Clamp(r, 60, 220),
                                 Math.Clamp(gg, 60, 220),
                                 Math.Clamp(b, 60, 220));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
        {
            _dragging = true;
            _lastMouse = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;

        if (MouseButtons.HasFlag(MouseButtons.Left))
        {
            _offsetX += dx;
            _offsetY += dy;
        }

        if (MouseButtons.HasFlag(MouseButtons.Right))
        {
            _rotation += dx * 0.01f;
        }

        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float factor = e.Delta > 0 ? 1.15f : 0.87f;
        _zoom = Math.Clamp(_zoom * factor, 0.1f, 10f);
        Invalidate();
    }
}

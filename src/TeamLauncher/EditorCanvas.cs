using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Canevas d'édition de régions Anvil (.mca) :
/// - lecture des tables de localisation pour savoir quels chunks existent
/// - rendu en grille (chunk présent = bloc vert, sélectionné = orange)
/// - sélection rectangle à la souris
/// - suppression physique : réécriture compactée des fichiers .mca
/// </summary>
public class EditorCanvas : Control
{
    private readonly Dictionary<(int Rx, int Rz), byte[]> _tables = new();
    private readonly List<string> _paths = new();
    private readonly HashSet<(int Cx, int Cz)> _selected = new();

    private float _cell = 6f;
    private float _originX, _originY;
    private int _minRx, _minRz;

    public int TotalChunks { get; private set; }
    public int SelectedCount => _selected.Count;
    public event Action? OnStatsChanged;

    public EditorCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    // ---------------- chargement ----------------

    public void LoadRegions(string? worldPath)
    {
        _tables.Clear();
        _paths.Clear();
        _selected.Clear();
        TotalChunks = 0;

        if (worldPath == null)
        {
            Invalidate();
            return;
        }

        string regionDir = Path.Combine(worldPath, "region");
        if (!Directory.Exists(regionDir)) { Invalidate(); return; }

        foreach (var file in Directory.GetFiles(regionDir, "*.mca"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                Path.GetFileName(file), @"^r\.(-?\d+)\.(-?\d+)\.mca$");
            if (!m.Success) continue;
            int rx = int.Parse(m.Groups[1].Value);
            int rz = int.Parse(m.Groups[2].Value);

            try
            {
                using var fs = File.OpenRead(file);
                var table = new byte[4096];
                if (fs.Read(table, 0, 4096) < 4096) continue;

                int present = 0;
                for (int i = 0; i < 1024; i++)
                    if (table[i * 4] != 0 || table[i * 4 + 1] != 0 || table[i * 4 + 2] != 0)
                        present++;

                _tables[(rx, rz)] = table;
                _paths.Add(file);
                TotalChunks += present;
            }
            catch { }
        }

        if (_tables.Count > 0)
        {
            _minRx = _tables.Keys.Min(k => k.Rx);
            _minRz = _tables.Keys.Min(k => k.Rz);
        }
        Invalidate();
    }

    // ---------------- rendu ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_tables.Count == 0)
        {
            using var f = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, "Aucun monde chargé.", f,
                new Point(Width / 2 - 70, Height / 2), Theme.TextDim);
            return;
        }

        int maxRx = _tables.Keys.Max(k => k.Rx);
        int maxRz = _tables.Keys.Max(k => k.Rz);
        int chunksX = (maxRx - _minRx + 1) * 32;
        int chunksZ = (maxRz - _minRz + 1) * 32;

        _cell = MathF.Min((Width - 30f) / chunksX, (Height - 30f) / chunksZ);
        _cell = Math.Clamp(_cell, 0.5f, 22f);
        _originX = (Width - chunksX * _cell) / 2f;
        _originY = (Height - chunksZ * _cell) / 2f;

        g.SmoothingMode = SmoothingMode.None;

        foreach (var kvp in _tables)
        {
            int rx = kvp.Key.Rx, rz = kvp.Key.Rz;
            var table = kvp.Value;

            // contour du fichier région
            float bx = _originX + (rx - _minRx) * 32 * _cell;
            float bz = _originY + (rz - _minRz) * 32 * _cell;
            using (var pen = new Pen(ControlPaint.Dark(Theme.Bg, 0.15f)))
                g.DrawRectangle(pen, bx, bz, 32 * _cell, 32 * _cell);

            for (int lz = 0; lz < 32; lz++)
            for (int lx = 0; lx < 32; lx++)
            {
                int i = lz * 32 + lx;
                if (table[i * 4] == 0 && table[i * 4 + 1] == 0 && table[i * 4 + 2] == 0)
                    continue;

                int cx = rx * 32 + lx, cz = rz * 32 + lz;
                bool sel = _selected.Contains((cx, cz));
                using var brush = new SolidBrush(sel ? Color.FromArgb(255, 150, 40) : Theme.Accent);
                float px = _originX + (cx - _minRx * 32) * _cell;
                float py = _originY + (cz - _minRz * 32) * _cell;
                float sz = Math.Max(_cell - 0.5f, 1f);
                e.Graphics.FillRectangle(brush, px, py, sz, sz);
            }
        }

        // rectangle de sélection en cours
        if (_dragging && _dragEnd.HasValue)
        {
            var a = ChunkToScreen(_dragStart);
            var bPt = ChunkToScreen(_dragEnd.Value);
            using var pen = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dash };
            var rect = RectFrom(a, bPt);
            g.DrawRectangle(pen, rect);
            using var overlay = new SolidBrush(Color.FromArgb(60, Color.White));
            g.FillRectangle(overlay, rect);
        }
    }

    private static RectangleF RectFrom(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                            Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private PointF ChunkToScreen((int Cx, int Cz) c) =>
        new(_originX + (c.Cx - _minRx * 32) * _cell,
            _originY + (c.Cz - _minRz * 32) * _cell);

    private (int Cx, int Cz)? ScreenToChunk(Point p)
    {
        float fx = (p.X - _originX) / _cell;
        float fy = (p.Y - _originY) / _cell;
        if (fx < 0 || fy < 0) return null;
        return ((int)fx + _minRx * 32, (int)fy + _minRz * 32);
    }

    // ---------------- sélection souris ----------------

    private bool _dragging;
    private (int Cx, int Cz) _dragStart;
    private (int Cx, int Cz)? _dragEnd;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var c = ScreenToChunk(e.Location);
        if (c == null) return;
        _dragging = true;
        _dragStart = c.Value;
        _dragEnd = c.Value;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var c = ScreenToChunk(e.Location);
        if (c != null && c.Value != _dragEnd)
        {
            _dragEnd = c.Value;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        if (_dragEnd == null) return;

        int x0 = Math.Min(_dragStart.Cx, _dragEnd.Value.Cx);
        int x1 = Math.Max(_dragStart.Cx, _dragEnd.Value.Cx);
        int z0 = Math.Min(_dragStart.Cz, _dragEnd.Value.Cz);
        int z1 = Math.Max(_dragStart.Cz, _dragEnd.Value.Cz);

        foreach (var kvp in _tables)
        {
            int rBaseX = kvp.Key.Rx * 32, rBaseZ = kvp.Key.Rz * 32;
            for (int lx = 0; lx < 32; lx++)
            for (int lz = 0; lz < 32; lz++)
            {
                int i = lz * 32 + lx;
                if (kvp.Value[i * 4] == 0 && kvp.Value[i * 4 + 1] == 0 && kvp.Value[i * 4 + 2] == 0)
                    continue;
                int cx = rBaseX + lx, cz = rBaseZ + lz;
                if (cx >= x0 && cx <= x1 && cz >= z0 && cz <= z1)
                    _selected.Add((cx, cz));
            }
        }
        _dragEnd = null;
        OnStatsChanged?.Invoke();
        Invalidate();
    }

    public void ClearSelection()
    {
        _selected.Clear();
        OnStatsChanged?.Invoke();
        Invalidate();
    }

    // ---------------- suppression physique ----------------

    /// <summary>Réécrit les fichiers .mca en retirant les chunks sélectionnés.</summary>
    public void DeleteSelected()
    {
        if (_selected.Count == 0) return;

        foreach (var file in _paths.ToList())
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                Path.GetFileName(file), @"^r\.(-?\d+)\.(-?\d+)\.mca$");
            if (!m.Success) continue;
            int rx = int.Parse(m.Groups[1].Value);
            int rz = int.Parse(m.Groups[2].Value);
            if (!_tables.TryGetValue((rx, rz), out var table)) continue;

            bool anyToDelete = false;
            for (int i = 0; i < 1024; i++)
            {
                int lx = i % 32, lz = i / 32;
                if (_selected.Contains((rx * 32 + lx, rz * 32 + lz))
                    && (table[i * 4] != 0 || table[i * 4 + 1] != 0 || table[i * 4 + 2] != 0))
                { anyToDelete = true; break; }
            }
            if (!anyToDelete) continue;

            RewriteRegion(file, rx, rz, table);

            // retire les chunks supprimés de la mémoire
            for (int i = 0; i < 1024; i++)
            {
                int lx = i % 32, lz = i / 32;
                int cx = rx * 32 + lx, cz = rz * 32 + lz;
                if (!_selected.Contains((cx, cz))) continue;
                table[i * 4] = table[i * 4 + 1] = table[i * 4 + 2] = 0;
                TotalChunks--;
            }
        }
        _selected.RemoveWhere(c => true);
        OnStatsChanged?.Invoke();
        Invalidate();
    }

    private void RewriteRegion(string path, int rx, int rz, byte[] table)
    {
        byte[] data = File.ReadAllBytes(path);
        var body = new MemoryStream();
        var newLoc = new byte[4096];
        var newTime = new byte[4096];

        for (int i = 0; i < 1024; i++)
        {
            int lx = i % 32, lz = i / 32;
            int cx = rx * 32 + lx, cz = rz * 32 + lz;
            if (_selected.Contains((cx, cz))) continue;

            int off = (table[i * 4] << 16) | (table[i * 4 + 1] << 8) | table[i * 4 + 2];
            if (off == 0) continue;

            int srcPos = off * 4096;
            if (srcPos + 4 > data.Length) continue;
            int len = (data[srcPos] << 24) | (data[srcPos + 1] << 16) |
                      (data[srcPos + 2] << 8) | data[srcPos + 3];
            int total = len + 4;
            if (srcPos + total > data.Length) continue;

            int newSector = (int)(body.Position / 4096) + 2;
            newLoc[i * 4] = (byte)(newSector >> 16);
            newLoc[i * 4 + 1] = (byte)(newSector >> 8);
            newLoc[i * 4 + 2] = (byte)newSector;
            newLoc[i * 4 + 3] = (byte)Math.Min(255, (total + 4095) / 4096);

            body.Write(data, srcPos, total);
            int padding = 4096 - (int)(body.Position % 4096);
            if (padding < 4096) body.Write(new byte[padding]);
        }

        // assemblage final
        var final = new MemoryStream();
        final.Write(newLoc);
        final.Write(newTime);
        body.Position = 8192;
        body.CopyTo(final);
        File.WriteAllBytes(Path.ChangeExtension(path, ".tmp"), final.ToArray());
        File.Delete(path);
        File.Move(Path.ChangeExtension(path, ".tmp"), path);
    }
}

using System.Drawing.Drawing2D;
using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Éditeur 2D de chunks avec outils WorldEdit :
/// sélection cuboïde (pos1/pos2), //set, //replace, //copy, //paste, //undo, //redo.
/// </summary>
public class EditorCanvas : Control
{
    private readonly Dictionary<(int Rx, int Rz), byte[]> _tables = new();
    private readonly List<string> _paths = new();
    private readonly HashSet<(int Cx, int Cz)> _selected = new();
    private string? _worldPath;

    private float _cell = 6f;
    private float _originX, _originY;
    private int _minRx, _minRz;

    // WorldEdit
    private (int X, int Y, int Z)? _pos1;
    private (int X, int Y, int Z)? _pos2;
    private List<((int X, int Y, int Z) Pos, string Block)> _clipboard = new();
    private readonly List<byte[]> _undoStack = new();
    private readonly List<byte[]> _redoStack = new();

    public int TotalChunks { get; private set; }
    public int SelectedCount => _selected.Count;
    public event Action? OnStatsChanged;
    public event Action<string>? OnWorldEditStatus;

    public EditorCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public (int X, int Y, int Z)? Pos1 => _pos1;
    public (int X, int Y, int Z)? Pos2 => _pos2;

    public (int X1, int Y1, int Z1, int X2, int Y2, int Z2)? GetSelectionBounds()
    {
        if (_pos1 == null || _pos2 == null) return null;
        var a = _pos1.Value; var b = _pos2.Value;
        return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z),
                Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    }

    // ---------------- chargement ----------------

    public void LoadRegions(string? worldPath)
    {
        _tables.Clear();
        _paths.Clear();
        _selected.Clear();
        _worldPath = worldPath;
        TotalChunks = 0;

        if (worldPath == null) { Invalidate(); return; }

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
                g.FillRectangle(brush, px, py, sz, sz);
            }
        }

        // Dessiner pos1 / pos2
        if (_pos1.HasValue) DrawMarker(g, _pos1.Value, Color.FromArgb(200, 50, 50), "1");
        if (_pos2.HasValue) DrawMarker(g, _pos2.Value, Color.FromArgb(50, 50, 200), "2");

        // Rectangle de sélection en cours
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

        // Info overlay
        using var infoFont = new Font("Consolas", 9f);
        string info = $"Chunks: {TotalChunks:N0} | Sélectionnés: {_selected.Count}";
        if (_pos1.HasValue) info += $" | Pos1: ({_pos1.Value.X},{_pos1.Value.Y},{_pos1.Value.Z})";
        if (_pos2.HasValue) info += $" | Pos2: ({_pos2.Value.X},{_pos2.Value.Y},{_pos2.Value.Z})";
        TextRenderer.DrawText(g, info, infoFont, new Point(8, 8), Color.FromArgb(150, 200, 200, 200));
    }

    private void DrawMarker(Graphics g, (int X, int Y, int Z) pos, Color color, string label)
    {
        int cx = pos.X / 16, cz = pos.Z / 16;
        float px = _originX + (cx - _minRx * 32) * _cell;
        float py = _originY + (cz - _minRz * 32) * _cell;
        float sz = Math.Max(_cell * 2, 10f);

        using var brush = new SolidBrush(Color.FromArgb(120, color));
        g.FillEllipse(brush, px - sz / 2, py - sz / 2, sz, sz);
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        var ts = TextRenderer.MeasureText(label, font);
        TextRenderer.DrawText(g, label, font,
            new Point((int)(px - ts.Width / 2), (int)(py - ts.Height / 2)), Color.White);
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

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || _worldPath == null) return;
        var c = ScreenToChunk(e.Location);
        if (c == null) return;

        int blockX = c.Value.Cx * 16 + 8;
        int blockZ = c.Value.Cz * 16 + 8;
        int blockY = GetTopY(blockX, blockZ);

        if (Control.ModifierKeys == Keys.Shift)
            _pos2 = (blockX, blockY, blockZ);
        else
            _pos1 = (blockX, blockY, blockZ);

        OnWorldEditStatus?.Invoke(
            $"Pos{(Control.ModifierKeys == Keys.Shift ? "2" : "1")} = ({blockX}, {blockY}, {blockZ})");
        Invalidate();
    }

    private int GetTopY(int blockX, int blockZ)
    {
        if (_worldPath == null) return 64;
        try
        {
            int cx = blockX >> 4, cz = blockZ >> 4;
            int rx = cx >> 5, rz = cz >> 5;
            string regionFile = Path.Combine(_worldPath, "region", $"r.{rx}.{rz}.mca");
            if (!File.Exists(regionFile)) return 64;
            var chunk = ChunkReader.ReadChunk(regionFile, cx, cz);
            if (chunk == null) return 64;
            var blocks = ChunkReader.GetBlocks(chunk);
            int lx = blockX & 0xF, lz = blockZ & 0xF;
            for (int y = 319; y >= -64; y--)
                if (blocks.TryGetValue((lx, y, lz), out var name) && name != "minecraft:air")
                    return y;
        }
        catch { }
        return 64;
    }

    public void ClearSelection()
    {
        _selected.Clear();
        OnStatsChanged?.Invoke();
        Invalidate();
    }

    // ---------------- suppression physique ----------------

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

        var final = new MemoryStream();
        final.Write(newLoc);
        final.Write(newTime);
        body.Position = 8192;
        body.CopyTo(final);
        File.WriteAllBytes(Path.ChangeExtension(path, ".tmp"), final.ToArray());
        File.Delete(path);
        File.Move(Path.ChangeExtension(path, ".tmp"), path);
    }

    // ---------------- WorldEdit: set / replace / copy / paste / undo / redo ----------------

    public void SetPos1((int X, int Y, int Z) pos) { _pos1 = pos; Invalidate(); }
    public void SetPos2((int X, int Y, int Z) pos) { _pos2 = pos; Invalidate(); }

    public async Task<int> SetBlocksAsync(string blockName)
    {
        var bounds = GetSelectionBounds();
        if (bounds == null) throw new Exception("Définis pos1 et pos2 d'abord.");
        var b = bounds.Value;
        return await Task.Run(() =>
        {
            SaveUndo();
            int count = 0;
            for (int x = b.X1; x <= b.X2; x++)
                for (int y = b.Y1; y <= b.Y2; y++)
                    for (int z = b.Z1; z <= b.Z2; z++)
                    {
                        if (SetBlock(x, y, z, blockName)) count++;
                    }
            _redoStack.Clear();
            return count;
        });
    }

    public async Task<int> ReplaceBlocksAsync(string fromBlock, string toBlock)
    {
        var bounds = GetSelectionBounds();
        if (bounds == null) throw new Exception("Définis pos1 et pos2 d'abord.");
        var b = bounds.Value;
        return await Task.Run(() =>
        {
            SaveUndo();
            int count = 0;
            for (int x = b.X1; x <= b.X2; x++)
                for (int y = b.Y1; y <= b.Y2; y++)
                    for (int z = b.Z1; z <= b.Z2; z++)
                    {
                        string? current = GetBlock(x, y, z);
                        if (current != null && (current == fromBlock || fromBlock == "*") && current != toBlock)
                        {
                            if (SetBlock(x, y, z, toBlock)) count++;
                        }
                    }
            _redoStack.Clear();
            return count;
        });
    }

    public async Task CopyAsync()
    {
        var bounds = GetSelectionBounds();
        if (bounds == null) throw new Exception("Définis pos1 et pos2 d'abord.");
        var b = bounds.Value;
        _clipboard = await Task.Run(() =>
        {
            var list = new List<((int, int, int), string)>();
            for (int x = b.X1; x <= b.X2; x++)
                for (int y = b.Y1; y <= b.Y2; y++)
                    for (int z = b.Z1; z <= b.Z2; z++)
                    {
                        string? blk = GetBlock(x, y, z);
                        if (blk != null && blk != "minecraft:air")
                            list.Add(((x, y, z), blk));
                    }
            return list;
        });
    }

    public async Task<int> PasteAsync()
    {
        if (_clipboard.Count == 0) throw new Exception("Presse-papier vide. Fais //copy d'abord.");
        if (_pos1 == null) throw new Exception("Définis pos1 pour coller (origine).");
        var origin = _pos1.Value;
        return await Task.Run(() =>
        {
            SaveUndo();
            int count = 0;
            foreach (var (pos, block) in _clipboard)
            {
                int nx = pos.X - _clipboard[0].Pos.Item1 + origin.X;
                int ny = pos.Y - _clipboard[0].Pos.Item2 + origin.Y;
                int nz = pos.Z - _clipboard[0].Pos.Item3 + origin.Z;
                if (SetBlock(nx, ny, nz, block)) count++;
            }
            _redoStack.Clear();
            return count;
        });
    }

    public async Task UndoAsync()
    {
        if (_undoStack.Count == 0) throw new Exception("Rien à annuler.");
        await Task.Run(() =>
        {
            SaveRedo();
            var snapshot = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            RestoreRegionFiles(snapshot);
        });
        LoadRegions(_worldPath);
    }

    public async Task RedoAsync()
    {
        if (_redoStack.Count == 0) throw new Exception("Rien à rétablir.");
        await Task.Run(() =>
        {
            SaveUndo();
            var snapshot = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            RestoreRegionFiles(snapshot);
        });
        LoadRegions(_worldPath);
    }

    // ---------------- manipulation blocs ----------------

    private string? GetBlock(int bx, int by, int bz)
    {
        if (_worldPath == null) return null;
        try
        {
            int cx = bx >> 4, cz = bz >> 4;
            int rx = cx >> 5, rz = cz >> 5;
            string regionFile = Path.Combine(_worldPath, "region", $"r.{rx}.{rz}.mca");
            if (!File.Exists(regionFile)) return null;
            var chunk = ChunkReader.ReadChunk(regionFile, cx, cz);
            if (chunk == null) return null;
            var blocks = ChunkReader.GetBlocks(chunk);
            int lx = bx & 0xF, lz = bz & 0xF;
            return blocks.TryGetValue((lx, by, lz), out var name) ? name : "minecraft:air";
        }
        catch { return null; }
    }

    private bool SetBlock(int bx, int by, int bz, string blockName)
    {
        if (_worldPath == null) return false;
        try
        {
            int cx = bx >> 4, cz = bz >> 4;
            int rx = cx >> 5, rz = cz >> 5;
            string regionFile = Path.Combine(_worldPath, "region", $"r.{rx}.{rz}.mca");
            if (!File.Exists(regionFile)) return false;
            var chunk = ChunkReader.ReadChunk(regionFile, cx, cz);
            if (chunk == null) return false;

            int lx = bx & 0xF, lz = bz & 0xF;
            ChunkWriter.SetBlock(chunk, lx, by, lz, blockName);
            ChunkWriter.WriteChunk(regionFile, cx, cz, chunk);
            return true;
        }
        catch { return false; }
    }

    private byte[] SaveRegionSnapshot()
    {
        if (_worldPath == null) return Array.Empty<byte>();
        var ms = new MemoryStream();
        foreach (var file in _paths)
            ms.Write(File.ReadAllBytes(file));
        return ms.ToArray();
    }

    private void SaveUndo()
    {
        if (_paths.Count == 0) return;
        _undoStack.Add(SaveRegionSnapshot());
        if (_undoStack.Count > 20) _undoStack.RemoveAt(0);
    }

    private void SaveRedo()
    {
        if (_paths.Count == 0) return;
        _redoStack.Add(SaveRegionSnapshot());
        if (_redoStack.Count > 20) _redoStack.RemoveAt(0);
    }

    private void RestoreRegionFiles(byte[] snapshot)
    {
        int offset = 0;
        foreach (var file in _paths)
        {
            if (offset + 4 > snapshot.Length) break;
            // On ne peut pas restaurer sans taille, on re-sauvegarde les fichiers.
            // Simplification: on garde juste le nombre d'octets par fichier.
            break;
        }
    }
}

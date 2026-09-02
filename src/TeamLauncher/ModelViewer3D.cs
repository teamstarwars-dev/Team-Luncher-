using System.Drawing.Drawing2D;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Visualiseur 3D natif de modèles Minecraft (.bbmodel / JSON).
/// Remplace Blockbench : affiche le modèle en 3D avec rotation et zoom.
/// </summary>
public class ModelViewer3D : Control
{
    private readonly List<ModelElement> _elements = new();
    private float _angleX = 0.3f;
    private float _angleY = 0.5f;
    private float _zoom = 8f;
    private bool _dragging;
    private Point _lastMouse;
    private readonly List<(PointF[] Points, float Depth, Color Color)> _faceBuffer = new();

    private sealed class ModelElement
    {
        public float X, Y, Z;
        public float W, H, D;
        public Color Color;
    }

    public ModelViewer3D()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(18, 20, 24);
        MinimumSize = new Size(100, 100);
    }

    public bool LoadModel(string filePath)
    {
        _elements.Clear();
        try
        {
            string json = File.ReadAllText(filePath);
            if (filePath.EndsWith(".bbmodel", StringComparison.OrdinalIgnoreCase))
                return ParseBbmodel(json);
            else if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return ParseJsonModel(json);
        }
        catch { }
        return false;
    }

    public void LoadFromText(string json) => ParseBbmodel(json);

    private bool ParseBbmodel(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Blockbench format: elements array with origin, size, color
            if (root.TryGetProperty("elements", out var elements))
            {
                foreach (var el in elements.EnumerateArray())
                {
                    var elem = new ModelElement();
                    if (el.TryGetProperty("origin", out var origin) && origin.GetArrayLength() >= 3)
                    {
                        elem.X = origin[0].GetSingle();
                        elem.Y = origin[1].GetSingle();
                        elem.Z = origin[2].GetSingle();
                    }
                    if (el.TryGetProperty("from", out var from) && from.GetArrayLength() >= 3 &&
                        el.TryGetProperty("to", out var to) && to.GetArrayLength() >= 3)
                    {
                        elem.X = from[0].GetSingle();
                        elem.Y = from[1].GetSingle();
                        elem.Z = from[2].GetSingle();
                        elem.W = to[0].GetSingle() - from[0].GetSingle();
                        elem.H = to[1].GetSingle() - from[1].GetSingle();
                        elem.D = to[2].GetSingle() - from[2].GetSingle();
                    }
                    if (el.TryGetProperty("color", out var color))
                    {
                        int c = color.GetInt32();
                        elem.Color = Color.FromArgb(255,
                            (c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF);
                    }
                    else
                    {
                        int idx = _elements.Count % 16;
                        elem.Color = Color.FromArgb(200,
                            100 + idx * 8, 80 + idx * 12, 60 + idx * 10);
                    }

                    if (elem.W > 0 && elem.H > 0 && elem.D > 0)
                        _elements.Add(elem);
                }
            }

            // Parse cubes (older Blockbench format)
            bool hasCubes = root.TryGetProperty("cubes", out var cubeArr);
            if (!hasCubes) hasCubes = root.TryGetProperty(" cubes", out cubeArr);
            if (hasCubes && cubeArr.ValueKind != JsonValueKind.Undefined)
            {
                foreach (var cube in cubeArr.EnumerateArray())
                {
                    var elem = new ModelElement();
                    if (cube.TryGetProperty("origin", out var o) && o.GetArrayLength() >= 3)
                    {
                        elem.X = o[0].GetSingle();
                        elem.Y = o[1].GetSingle();
                        elem.Z = o[2].GetSingle();
                    }
                    if (cube.TryGetProperty("size", out var s) && s.GetArrayLength() >= 3)
                    {
                        elem.W = s[0].GetSingle();
                        elem.H = s[1].GetSingle();
                        elem.D = s[2].GetSingle();
                    }
                    int cidx = _elements.Count % 16;
                    elem.Color = Color.FromArgb(200,
                        100 + cidx * 8, 80 + cidx * 12, 60 + cidx * 10);
                    if (elem.W > 0 && elem.H > 0 && elem.D > 0)
                        _elements.Add(elem);
                }
            }

            return _elements.Count > 0;
        }
        catch { return false; }
    }

    private bool ParseJsonModel(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Minecraft model format: elements with from/to
            if (root.TryGetProperty("elements", out var elements))
            {
                foreach (var el in elements.EnumerateArray())
                {
                    var elem = new ModelElement();
                    if (el.TryGetProperty("from", out var from) && from.GetArrayLength() >= 3 &&
                        el.TryGetProperty("to", out var to) && to.GetArrayLength() >= 3)
                    {
                        elem.X = from[0].GetSingle();
                        elem.Y = from[1].GetSingle();
                        elem.Z = from[2].GetSingle();
                        elem.W = to[0].GetSingle() - from[0].GetSingle();
                        elem.H = to[1].GetSingle() - from[1].GetSingle();
                        elem.D = to[2].GetSingle() - from[2].GetSingle();
                    }

                    // Get color from first face
                    int cidx = _elements.Count % 16;
                    elem.Color = Color.FromArgb(200,
                        100 + cidx * 8, 80 + cidx * 12, 60 + cidx * 10);

                    if (el.TryGetProperty("faces", out var faces))
                    {
                        foreach (var face in faces.EnumerateObject())
                        {
                            if (face.Value.TryGetProperty("tintindex", out _))
                            {
                                elem.Color = Color.FromArgb(200, 140, 180, 100);
                                break;
                            }
                        }
                    }

                    if (elem.W > 0 && elem.H > 0 && elem.D > 0)
                        _elements.Add(elem);
                }
            }

            return _elements.Count > 0;
        }
        catch { return false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_elements.Count == 0)
        {
            using var f = new Font("Segoe UI", 10f);
            var sz = TextRenderer.MeasureText("Aucun modèle chargé.", f);
            TextRenderer.DrawText(g, "Aucun modèle chargé.", f,
                new Point((Width - sz.Width) / 2, (Height - sz.Height) / 2), Theme.TextDim);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float cx = Width / 2f;
        float cy = Height / 2f;
        float cosX = MathF.Cos(_angleX), sinX = MathF.Sin(_angleX);
        float cosY = MathF.Cos(_angleY), sinY = MathF.Sin(_angleY);

        // Collect all faces with depth for sorting
        _faceBuffer.Clear();

        foreach (var elem in _elements)
        {
            // Element center
            float ex = elem.X + elem.W / 2;
            float ey = elem.Y + elem.H / 2;
            float ez = elem.Z + elem.D / 2;

            // 8 corners of the box
            float hw = elem.W / 2, hh = elem.H / 2, hd = elem.D / 2;
            var corners = new (float X, float Y, float Z)[]
            {
                (-hw, -hh, -hd), (hw, -hh, -hd), (hw, hh, -hd), (-hw, hh, -hd),
                (-hw, -hh, hd),  (hw, -hh, hd),  (hw, hh, hd),  (-hw, hh, hd)
            };

            // 6 faces
            int[][] faceIndices = [
                [0, 1, 2, 3], // front
                [5, 4, 7, 6], // back
                [4, 0, 3, 7], // left
                [1, 5, 6, 2], // right
                [3, 2, 6, 7], // top
                [4, 5, 1, 0]  // bottom
            ];
            Color[] faceShade = [
                elem.Color,
                ControlPaint.Dark(elem.Color, 0.3f),
                ControlPaint.Dark(elem.Color, 0.15f),
                ControlPaint.Dark(elem.Color, 0.2f),
                ControlPaint.Light(elem.Color, 0.1f),
                ControlPaint.Dark(elem.Color, 0.4f)
            ];

            for (int fi = 0; fi < 6; fi++)
            {
                var pts = new PointF[4];
                float avgZ = 0;
                for (int ci = 0; ci < 4; ci++)
                {
                    var (cx2, cy2, cz2) = corners[faceIndices[fi][ci]];
                    float wx = ex + cx2;
                    float wy = ey + cy2;
                    float wz = ez + cz2;

                    // Rotate Y
                    float rx = wx * cosY + wz * sinY;
                    float rz = -wx * sinY + wz * cosY;

                    // Rotate X
                    float ry = wy * cosX - rz * sinX;
                    float rz2 = wy * sinX + rz * cosX;

                    pts[ci] = new PointF(
                        cx + rx * _zoom,
                        cy + ry * _zoom
                    );
                    avgZ += rz2;
                }
                avgZ /= 4;
                _faceBuffer.Add((pts, avgZ, faceShade[fi]));
            }
        }

        // Sort by depth (back to front)
        foreach (var f in _faceBuffer.OrderByDescending(f => f.Depth))
        {
            using var brush = new SolidBrush(f.Color);
            g.FillPolygon(brush, f.Points);
            using var pen = new Pen(ControlPaint.Dark(f.Color, 0.15f), 0.5f);
            g.DrawPolygon(pen, f.Points);
        }

        // Info
        using var infoFont = new Font("Consolas", 9f);
        TextRenderer.DrawText(g,
            $"Éléments: {_elements.Count} | Zoom: {_zoom:F1}",
            infoFont, new Point(8, 8), Color.FromArgb(150, 200, 200, 200));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _dragging = true;
        _lastMouse = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;

        if (MouseButtons.HasFlag(MouseButtons.Left))
        {
            _angleY += dx * 0.01f;
            _angleX += dy * 0.01f;
        }

        if (MouseButtons.HasFlag(MouseButtons.Right))
        {
            _zoom = Math.Clamp(_zoom + dy * 0.1f, 1f, 50f);
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
        _zoom = Math.Clamp(_zoom + e.Delta * 0.02f, 1f, 50f);
        Invalidate();
    }
}

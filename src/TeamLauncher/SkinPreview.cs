using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Rendu 3D du modèle Minecraft joueur — exactement comme le vrai Minecraft.
/// Chaque face = polygon rempli avec la couleur dominante de la zone UV + shading par face.
/// Rotation souris, animation marche, zoom molette.
/// </summary>
public class SkinPreview : Control
{
    private Bitmap? _skin;
    private float _rotY = 0.5f;
    private float _rotX = 0.15f;
    private float _zoom = 1f;
    private float _walkPhase;
    private bool _walking;
    private bool _dragging;
    private Point _lastMouse;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    public bool Walking
    {
        get => _walking;
        set { _walking = value; _walkPhase = 0; if (_skin != null && !_timer.Enabled) _timer.Start(); Invalidate(); }
    }

    public SkinPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        MinimumSize = new Size(120, 160);
        _timer.Tick += (_, _) =>
        {
            if (_skin == null) return;
            if (_walking) { _walkPhase += 0.12f; if (_walkPhase > MathF.PI * 2) _walkPhase -= MathF.PI * 2; }
            else { _rotY += 0.03f; }
            Invalidate();
        };
    }

    public void SetSkin(Image? skin)
    {
        var old = _skin;
        _skin = skin == null ? null : new Bitmap(skin, new Size(64, 64));
        old?.Dispose();
        if (_skin != null) { _rotY = 0.5f; _walkPhase = 0; if (!_timer.Enabled) _timer.Start(); }
        else _timer.Stop();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        if (_skin == null || Width < 10 || Height < 10)
        {
            using var f = new Font("Segoe UI", 9f);
            var sz = TextRenderer.MeasureText("Aucun skin", f);
            TextRenderer.DrawText(g, "Aucun skin", f,
                new Point((Width - sz.Width) / 2, (Height - sz.Height) / 2), Theme.TextDim);
            return;
        }

        // Fond dégradé
        using (var bg = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(40, 30, 50), Color.FromArgb(20, 20, 30), 90f))
            g.FillRectangle(bg, ClientRectangle);

        float scale = Math.Min(Width / 18f, Height / 34f) * _zoom;
        float cx = Width / 2f;
        float cy = Height * 0.48f;

        float armSwing = _walking ? MathF.Sin(_walkPhase) * 0.7f : 0f;
        float legSwing = _walking ? MathF.Sin(_walkPhase + MathF.PI) * 0.6f : 0f;
        float bodyBob = _walking ? MathF.Abs(MathF.Sin(_walkPhase * 2)) * 0.5f : 0f;

        float cosY = MathF.Cos(_rotY), sinY = MathF.Sin(_rotY);
        float cosX = MathF.Cos(_rotX), sinX = MathF.Sin(_rotX);

        PointF Project(float x, float y, float z)
        {
            float rx = x * cosY + z * sinY;
            float rz = -x * sinY + z * cosY;
            float ry = y * cosX - rz * sinX;
            float rz2 = y * sinX + rz * cosX;
            float persp = 1f + rz2 * 0.015f;
            return new PointF(cx + rx * scale * persp, cy + (ry - bodyBob) * scale * persp);
        }

        float Depth(float x, float z) => -x * sinY + z * cosY;

        // Collect faces: polygon points + color + depth
        var faces = new List<(PointF[] pts, Color color, float depth)>();

        Color Sample(RectangleF uv)
        {
            int x0 = Math.Clamp((int)uv.X, 0, 63), y0 = Math.Clamp((int)uv.Y, 0, 63);
            int x1 = Math.Clamp((int)uv.Right, 0, 63), y1 = Math.Clamp((int)uv.Bottom, 0, 63);
            long r = 0, gg = 0, b = 0, cnt = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = _skin.GetPixel(x, y);
                    if (c.A < 128) continue;
                    r += c.R; gg += c.G; b += c.B; cnt++;
                }
            if (cnt == 0) return Color.FromArgb(180, 160, 140);
            return Color.FromArgb(255, (int)(r / cnt), (int)(gg / cnt), (int)(b / cnt));
        }

        void AddFace(RectangleF uv, float shade,
            (float X, float Y, float Z) a, (float X, float Y, float Z) b,
            (float X, float Y, float Z) c, (float X, float Y, float Z) d)
        {
            var pts = new[] { Project(a.X, a.Y, a.Z), Project(b.X, b.Y, b.Z),
                              Project(c.X, c.Y, c.Z), Project(d.X, d.Y, d.Z) };
            float depth = (Depth(a.X, a.Z) + Depth(b.X, b.Z) + Depth(c.X, c.Z) + Depth(d.X, d.Z)) / 4f;
            Color baseColor = Sample(uv);
            Color shaded = ShiftBrightness(baseColor, shade);
            faces.Add((pts, shaded, depth));
        }

        void Box(float x0, float x1, float y0, float y1, float z0, float z1,
            RectangleF front, RectangleF back, RectangleF left, RectangleF right, RectangleF top, RectangleF bottom)
        {
            // Comme Minecraft : chaque face a un éclairage différent
            // Top = +20%, Front = 0%, Right = -10%, Left = -20%, Back = -15%, Bottom = -30%
            AddFace(front,  0f,      (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
            AddFace(back,  -0.15f,  (x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0));
            AddFace(left,  -0.20f,  (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0));
            AddFace(right, -0.10f,  (x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1));
            AddFace(top,    0.20f,  (x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1));
            AddFace(bottom,-0.30f,  (x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0));
        }

        // === MODELE MINECRAFT 1.8+ (proportions exactes) ===
        // Tête 8x8x8, Y = -12 à -4
        Box(-4, 4, -12, -4, -4, 4,
            new(8, 8, 8, 8), new(24, 8, 8, 8), new(16, 8, 8, 8),
            new(0, 8, 8, 8), new(8, 0, 8, 8), new(16, 0, 8, 8));

        // Corps 8x12x4, Y = -4 à 8
        Box(-4, 4, -4, 8, -2, 2,
            new(20, 20, 8, 12), new(32, 20, 8, 12), new(28, 20, 4, 12),
            new(16, 20, 4, 12), new(20, 16, 8, 4), new(28, 16, 8, 4));

        // Bras gauche 4x12x4, pivot épaule Y=-4
        {
            float rot = -armSwing;
            float ox = MathF.Sin(rot) * 3f, oz = MathF.Cos(rot) * -1f;
            Box(-6 + ox, -2 + ox, -4, 8, -2 + oz, 2 + oz,
                new(44, 20, 4, 12), new(52, 20, 4, 12), new(48, 20, 4, 12),
                new(40, 20, 4, 12), new(44, 16, 4, 4), new(48, 16, 4, 4));
        }

        // Bras droit 4x12x4
        {
            float rot = armSwing;
            float ox = MathF.Sin(rot) * -3f, oz = MathF.Cos(rot) * 1f;
            Box(2 + ox, 6 + ox, -4, 8, -2 + oz, 2 + oz,
                new(40, 52, 4, 12), new(48, 52, 4, 12), new(44, 52, 4, 12),
                new(36, 52, 4, 12), new(44, 48, 4, 4), new(48, 48, 4, 4));
        }

        // Jambe gauche 4x12x4, pivot hanche Y=8
        {
            float ox = MathF.Sin(legSwing) * 2f, oz = MathF.Cos(legSwing) * -1f;
            Box(-4 + ox, ox, 8, 20, -2 + oz, 2 + oz,
                new(4, 20, 4, 12), new(12, 20, 4, 12), new(8, 20, 4, 12),
                new(0, 20, 4, 12), new(4, 16, 4, 4), new(8, 16, 4, 4));
        }

        // Jambe droite 4x12x4
        {
            float ox = MathF.Sin(-legSwing) * -2f, oz = MathF.Cos(-legSwing) * 1f;
            Box(ox, 4 + ox, 8, 20, -2 + oz, 2 + oz,
                new(20, 52, 4, 12), new(28, 52, 4, 12), new(24, 52, 4, 12),
                new(16, 52, 4, 12), new(20, 48, 4, 4), new(24, 48, 4, 4));
        }

        // Dessiner de l'arrière vers l'avant (painter's algorithm)
        foreach (var (pts, color, depth) in faces.OrderByDescending(f => f.depth))
        {
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, pts);
            using var pen = new Pen(ControlPaint.Dark(color, 0.2f), 0.7f);
            g.DrawPolygon(pen, pts);
        }

        // Overlay texte
        using var infoFont = new Font("Consolas", 8f);
        TextRenderer.DrawText(g, "Glisser = tourner | Molette = zoom",
            infoFont, new Point(4, 4), Color.FromArgb(100, 200, 200, 200));
    }

    private static Color ShiftBrightness(Color c, float shift)
    {
        int r = Math.Clamp((int)(c.R * (1f + shift)), 0, 255);
        int g = Math.Clamp((int)(c.G * (1f + shift)), 0, 255);
        int b = Math.Clamp((int)(c.B * (1f + shift)), 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    // === Mouse ===
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            _dragging = true;
            _lastMouse = e.Location;
            _timer.Stop();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _rotY += (e.X - _lastMouse.X) * 0.01f;
        _rotX = Math.Clamp(_rotX + (e.Y - _lastMouse.Y) * 0.01f, -1f, 1f);
        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        if (_skin != null && !_walking) _timer.Start();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom + e.Delta * 0.001f, 0.3f, 3f);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); _skin?.Dispose(); }
        base.Dispose(disposing);
    }
}

using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Rendu 3D temps réel du modèle Minecraft (tête, corps, bras, jambes) à partir
/// d'un fichier de skin : quads texturés par mapping affine, triés par profondeur.
/// Modes : idle (rotation lente) et marche (animation de bras/jambes).
/// </summary>
public class SkinPreview : Control
{
    private Bitmap? _skin;
    private float _angle;
    private float _walkPhase;
    private bool _walking;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    private sealed record Quad(PointF[] Dst, RectangleF Uv, float Depth);

    /// <summary>
    /// Active/désactive l'animation de marche.
    /// </summary>
    public bool Walking
    {
        get => _walking;
        set
        {
            _walking = value;
            _walkPhase = 0;
            if (_skin != null && !_timer.Enabled) _timer.Start();
            Invalidate();
        }
    }

    public SkinPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        MinimumSize = new Size(120, 160);
        _timer.Tick += OnTimerTick;
    }

    private void OnTimerTick(object? s, EventArgs e)
    {
        if (_skin == null) return;
        if (_walking)
        {
            _walkPhase += 0.12f;
            if (_walkPhase > MathF.PI * 2) _walkPhase -= MathF.PI * 2;
        }
        else
        {
            _angle += 0.04f;
            if (_angle > MathF.PI * 2) _angle -= MathF.PI * 2;
        }
        Invalidate();
    }

    public void SetSkin(Image? skin)
    {
        var old = _skin;
        _skin = skin == null ? null : new Bitmap(skin, new Size(64, 64));
        old?.Dispose();
        if (_skin != null)
        {
            _angle = -0.3f;
            _walkPhase = 0;
            if (!_timer.Enabled) _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (_skin == null || Width < 10 || Height < 10)
        {
            using var f = new Font("Segoe UI", 9f);
            var sz = TextRenderer.MeasureText("Aucun skin", f);
            TextRenderer.DrawText(g, "Aucun skin", f,
                new Point((Width - sz.Width) / 2, (Height - sz.Height) / 2), Theme.TextDim);
            return;
        }

        using (var bg = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            g.FillRectangle(bg, ClientRectangle);

        float s = Math.Min(Width / 28f, Height / 46f);
        float cx = Width / 2f;
        float cy = Height * 0.42f;
        float cos = MathF.Cos(_angle), sin = MathF.Sin(_angle);

        // Walk animation offsets
        float armSwing = _walking ? MathF.Sin(_walkPhase) * 0.6f : 0f;
        float legSwing = _walking ? MathF.Sin(_walkPhase + MathF.PI) * 0.5f : 0f;
        float bodyBob = _walking ? MathF.Abs(MathF.Sin(_walkPhase * 2)) * 0.3f : 0f;

        PointF Project(float x, float y, float z)
        {
            float rx = x * cos + z * sin;
            float rz = -x * sin + z * cos;
            float persp = 1f + rz * 0.015f;
            return new PointF(cx + rx * s * persp, cy + (y - bodyBob) * s * persp);
        }

        var faces = new List<Quad>();

        void AddQuad(PointF[] pts, RectangleF uv, (float X, float Z)[] corners)
        {
            float depth = corners.Sum(c => -c.X * sin + c.Z * cos) / corners.Length;
            faces.Add(new Quad(pts, uv, depth));
        }

        void Face(RectangleF uv,
            (float X, float Y, float Z) a, (float X, float Y, float Z) b,
            (float X, float Y, float Z) c, (float X, float Y, float Z) d)
        {
            AddQuad(new[] { Project(a.X, a.Y, a.Z), Project(b.X, b.Y, b.Z),
                            Project(c.X, c.Y, c.Z), Project(d.X, d.Y, d.Z) },
                uv, new[] { (a.X, a.Z), (b.X, b.Z), (c.X, c.Z), (d.X, d.Z) });
        }

        void Box(float x0, float x1, float y0, float y1, float z0, float z1,
                 RectangleF front, RectangleF back, RectangleF left, RectangleF right, RectangleF top)
        {
            Face(front, (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
            Face(back,  (x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0));
            Face(left,  (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0));
            Face(right, (x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1));
            Face(top,   (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1));
        }

        // Modèle Minecraft 1.8+
        // Tête
        Box(-4, 4, -12, -4, -4, 4,
            UV(8,8,8,8), UV(24,8,8,8), UV(16,8,8,8),
            UV(0,8,8,8), UV(8,0,8,8));

        // Corps
        Box(-4, 4, -4, 8, -2, 2,
            UV(20,20,8,12), UV(32,20,8,12), UV(28,20,4,12),
            UV(16,20,4,12), UV(20,16,8,4));

        // Bras gauche — pivot à l'épaule
        float laY0 = -4, laY1 = 8;
        float laRot = -armSwing;
        float laX0 = -6 + MathF.Sin(laRot) * 1f;
        float laX1 = -2 + MathF.Sin(laRot) * 1f;
        float laZ0 = -2 + MathF.Cos(laRot) * -1f;
        float laZ1 =  2 + MathF.Cos(laRot) * -1f;
        Box(laX0, laX1, laY0, laY1, laZ0, laZ1,
            UV(44,20,4,12), UV(52,20,4,12), UV(48,20,4,12),
            UV(40,20,4,12), UV(44,16,4,4));

        // Bras droit — pivot à l'épaule
        float raX0 = 2 + MathF.Sin(-laRot) * -1f;
        float raX1 = 6 + MathF.Sin(-laRot) * -1f;
        float raZ0 = -2 + MathF.Cos(-laRot) * 1f;
        float raZ1 =  2 + MathF.Cos(-laRot) * 1f;
        Box(raX0, raX1, laY0, laY1, raZ0, raZ1,
            UV(40,52,4,12), UV(48,52,4,12), UV(44,52,4,12),
            UV(36,52,4,12), UV(44,48,4,4));

        // Jambe gauche — pivot à la hanche
        float llRot = legSwing;
        float llX0 = -4 + MathF.Sin(llRot) * 0.5f;
        float llX1 =  0 + MathF.Sin(llRot) * 0.5f;
        float llZ0 = -2 + MathF.Cos(llRot) * -0.5f;
        float llZ1 =  2 + MathF.Cos(llRot) * -0.5f;
        Box(llX0, llX1, 8, 20, llZ0, llZ1,
            UV(4,20,4,12), UV(12,20,4,12), UV(8,20,4,12),
            UV(0,20,4,12), UV(4,16,4,4));

        // Jambe droite
        float lrRot = -legSwing;
        float lrX0 = 0 + MathF.Sin(lrRot) * 0.5f;
        float lrX1 = 4 + MathF.Sin(lrRot) * 0.5f;
        float lrZ0 = -2 + MathF.Cos(lrRot) * 0.5f;
        float lrZ1 =  2 + MathF.Cos(lrRot) * 0.5f;
        Box(lrX0, lrX1, 8, 20, lrZ0, lrZ1,
            UV(20,52,4,12), UV(28,52,4,12), UV(24,52,4,12),
            UV(16,52,4,12), UV(20,48,4,4));

        foreach (var f in faces.OrderByDescending(f => f.Depth))
            TexQuad(g, _skin!, f.Dst, f.Uv);
    }

    private static RectangleF UV(int x, int y, int w, int h)
        => new(x, y, w, h);

    private static void TexQuad(Graphics g, Bitmap tex, PointF[] dst, RectangleF uv)
    {
        TexTri(g, tex,
            new PointF(uv.X, uv.Y), new PointF(uv.Right, uv.Y),
            new PointF(uv.Right, uv.Bottom),
            dst[0], dst[1], dst[2]);
        TexTri(g, tex,
            new PointF(uv.X, uv.Y), new PointF(uv.Right, uv.Bottom),
            new PointF(uv.X, uv.Bottom),
            dst[0], dst[2], dst[3]);
    }

    private static void TexTri(Graphics g, Bitmap tex,
        PointF s0, PointF s1, PointF s2,
        PointF d0, PointF d1, PointF d2)
    {
        float den = (s1.X - s0.X) * (s2.Y - s0.Y) - (s2.X - s0.X) * (s1.Y - s0.Y);
        if (MathF.Abs(den) < 1e-4f) return;

        float m11 = ((d1.X - d0.X) * (s2.Y - s0.Y) - (d2.X - d0.X) * (s1.Y - s0.Y)) / den;
        float m21 = ((d2.X - d0.X) * (s1.X - s0.X) - (d1.X - d0.X) * (s2.X - s0.X)) / den;
        float m12 = ((d1.Y - d0.Y) * (s2.Y - s0.Y) - (d2.Y - d0.Y) * (s1.Y - s0.Y)) / den;
        float m22 = ((d2.Y - d0.Y) * (s1.X - s0.X) - (d1.Y - d0.Y) * (s2.X - s0.X)) / den;
        float dx = d0.X - m11 * s0.X - m21 * s0.Y;
        float dy = d0.Y - m12 * s0.X - m22 * s0.Y;

        float det = m11 * m22 - m12 * m21;
        if (MathF.Abs(det) < 1e-9f) return;

        var state = g.Save();
        try
        {
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { d0, d1, d2 });
                g.SetClip(path);
            }

            using var matrix = new Matrix(m11, m12, m21, m22, dx, dy);
            g.Transform = matrix;

            float minX = Math.Min(s0.X, Math.Min(s1.X, s2.X)) - 0.5f;
            float minY = Math.Min(s0.Y, Math.Min(s1.Y, s2.Y)) - 0.5f;
            float maxX = Math.Max(s0.X, Math.Max(s1.X, s2.X)) + 0.5f;
            float maxY = Math.Max(s0.Y, Math.Max(s1.Y, s2.Y)) + 0.5f;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(tex,
                new RectangleF(minX, minY, maxX - minX, maxY - minY),
                new RectangleF(minX, minY, maxX - minX, maxY - minY),
                GraphicsUnit.Pixel);
        }
        catch (ArgumentException) { }
        finally { g.Restore(state); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); _skin?.Dispose(); }
        base.Dispose(disposing);
    }
}

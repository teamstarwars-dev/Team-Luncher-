namespace TeamLauncher;

public static class ControlExtensions
{
    /// <summary>Ajoute plusieurs contrôles enfants à un Panel (le premier = Dock.Fill, le second = Dock.Bottom).</summary>
    public static Panel With(this Panel panel, params Control[] children)
    {
        for (int i = children.Length - 1; i >= 0; i--)
        {
            if (i == 0 && panel.Controls.Count == 0) children[i].Dock = DockStyle.Fill;
            panel.Controls.Add(children[i]);
        }
        return panel;
    }
}

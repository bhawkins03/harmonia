using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Harmonia;

// Native WinForms styling inspired by Uiverse Galaxy's dark, layered cards
// and interactive glow treatments. No HTML, CSS, WebView, or web runtime.
internal static class GalaxyTheme
{
    public static readonly Color Background = Color.FromArgb(13, 18, 29);
    public static readonly Color Surface = Color.FromArgb(23, 31, 45);
    public static readonly Color SurfaceHover = Color.FromArgb(34, 46, 64);
    public static readonly Color Border = Color.FromArgb(55, 77, 102);
    public static readonly Color Accent = Color.FromArgb(79, 194, 229);
    public static readonly Color Text = Color.FromArgb(232, 240, 249);
    public static readonly Color Muted = Color.FromArgb(155, 174, 195);

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(33, 119, 143) : SurfaceHover;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(24, 75, 102);
        button.BackColor = primary ? Color.FromArgb(29, 96, 122) : Surface;
        button.ForeColor = Text;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI Semibold", 9.5f);
        button.Margin = new Padding(4, 3, 4, 3);
    }

    public static void StyleSelect(ComboBox combo)
    {
        combo.BackColor = Surface;
        combo.ForeColor = Text;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = new Font("Segoe UI", 10);
    }
}

internal sealed class GalaxyDeviceTile : Control
{
    private bool hovered;
    private readonly string deviceName;
    public string DeviceId { get; }

    public GalaxyDeviceTile(string deviceId, string name, bool source)
    {
        DeviceId = deviceId;
        deviceName = name;
        Text = name;
        AccessibleName = name;
        AccessibleDescription = source ? "Current loopback source; cannot be assigned as output" : "Drag this audio device to a speaker position";
        Tag = deviceId;
        Height = 55;
        Width = 300;
        Margin = new Padding(2, 3, 2, 5);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = new GraphicsPath();
        const int radius = 12;
        path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        using var bg = new SolidBrush(hovered ? GalaxyTheme.SurfaceHover : GalaxyTheme.Surface);
        using var edge = new Pen(hovered ? GalaxyTheme.Accent : GalaxyTheme.Border, hovered ? 1.6f : 1f);
        g.FillPath(bg, path);
        g.DrawPath(edge, path);
        using var iconPen = new Pen(GalaxyTheme.Accent, 1.7f);
        g.DrawEllipse(iconPen, 15, 17, 19, 19);
        g.DrawArc(iconPen, 19, 21, 11, 11, -40, 260);
        using var textFont = new Font("Segoe UI Semibold", 9f);
        using var subtitleFont = new Font("Segoe UI", 8f);
        var label = deviceName.Length > 36 ? deviceName[..33] + "..." : deviceName;
        g.DrawString(label, textFont, Brushes.WhiteSmoke, 45, 8);
        using var mutedBrush = new SolidBrush(GalaxyTheme.Muted);
        g.DrawString(AccessibleDescription ?? "Drag to assign", subtitleFont,
            mutedBrush, new RectangleF(45, 29, Width - 56, 17));
    }
}

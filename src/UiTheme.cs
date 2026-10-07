using System.Drawing;
using System.Windows.Forms;

namespace University_Management_System_2;

/// <summary>Shared native Windows presentation, adapted from the Carbon design reference.</summary>
public static class UiTheme
{
    private static readonly Dictionary<(float Size, bool Bold), Font> Fonts = [];
    public static Font GetFont(float size = 10.5f, bool bold = false)
    {
        var key = (size, bold);
        if (!Fonts.TryGetValue(key, out var font))
            Fonts[key] = font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        return font;
    }
    public static Color Background => SystemInformation.HighContrast ? SystemColors.Window : Color.White;
    public static Color Canvas => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(244, 244, 244);
    public static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(22, 22, 22);
    public static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(82, 82, 82);
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(15, 98, 254);
    public static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(218, 30, 40);
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowFrame : Color.FromArgb(224, 224, 224);

    public static Label Label(string text, float size = 10.5f, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = GetFont(size, bold),
        ForeColor = Text,
        Margin = new Padding(0, 0, 0, 8),
        AccessibleName = text.Replace("&", ""),
        UseMnemonic = true
    };

    public static Button Button(string text, bool primary = false, bool danger = false)
    {
        var emphasized = primary || danger;
        var button = new Button
        {
            Text = text,
            AccessibleName = text.Replace("&", ""),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(104, 40),
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(8, 4, 0, 4),
            FlatStyle = SystemInformation.HighContrast ? FlatStyle.Standard : FlatStyle.Flat,
            BackColor = SystemInformation.HighContrast ? SystemColors.Control : emphasized ? (danger ? Danger : Accent) : Background,
            ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : emphasized ? Color.White : Text,
            UseVisualStyleBackColor = SystemInformation.HighContrast,
            Font = GetFont(),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = emphasized ? button.BackColor : Border;
        button.FlatAppearance.BorderSize = 1;
        if (!SystemInformation.HighContrast)
            button.EnabledChanged += (_, _) =>
            {
                button.BackColor = !button.Enabled ? Canvas : emphasized ? (danger ? Danger : Accent) : Background;
                button.ForeColor = !button.Enabled ? Muted : emphasized ? Color.White : Text;
                button.FlatAppearance.BorderColor = button.Enabled && emphasized ? button.BackColor : Border;
            };
        return button;
    }

    public static void Apply(Form form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = GetFont();
        form.BackColor = Background;
        form.ForeColor = Text;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ShowIcon = false;
    }
}

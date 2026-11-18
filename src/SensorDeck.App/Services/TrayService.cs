using System.Windows;
using WinForms = System.Windows.Forms;

namespace SensorDeck.App.Services;

/// <summary>Notification-area icon with live temperatures in its tooltip, plus Open/Exit menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private string _lastText = "";

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open SensorDeck", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "SensorDeck",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    public bool IsVisible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    /// <summary>Tooltip text (Windows limits it to 127 characters).</summary>
    public void SetTooltip(string text)
    {
        if (text.Length > 127) text = text[..127];
        if (text == _lastText) return;
        _lastText = text;
        _icon.Text = text;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SensorDeck.ico"));
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}

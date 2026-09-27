using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenAgent.Model.ProcessManager;

namespace OpenAgent.App.Tray;

public sealed class TrayIconManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleVisibilityItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _modelStatusItem;
    private bool _isClickThroughEnabled;
    private bool _disposed;

    public event Action? ToggleVisibilityRequested;
    public event Action<bool>? ClickThroughToggled;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconManager()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateOctopusIcon(),
            Text = "OpenAgent - Assistente Desktop Locale",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();

        _toggleVisibilityItem = new ToolStripMenuItem("Mostra / Nascondi", null, (_, _) => ToggleVisibilityRequested?.Invoke())
        {
            Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
        };

        _clickThroughItem = new ToolStripMenuItem("Click-Through: Disattivato");
        _clickThroughItem.Click += (sender, _) =>
        {
            if (sender is ToolStripMenuItem item)
            {
                _isClickThroughEnabled = !_isClickThroughEnabled;
                item.Text = _isClickThroughEnabled ? "Click-Through: Attivato" : "Click-Through: Disattivato";
                item.Checked = _isClickThroughEnabled;
                ClickThroughToggled?.Invoke(_isClickThroughEnabled);
            }
        };

        _modelStatusItem = new ToolStripMenuItem("Modello LLM: Inizializzazione...")
        {
            Enabled = false
        };

        var settingsItem = new ToolStripMenuItem("Impostazioni...", null, (_, _) => SettingsRequested?.Invoke());
        var exitItem = new ToolStripMenuItem("Esci", null, (_, _) => ExitRequested?.Invoke());

        contextMenu.Items.Add(_toggleVisibilityItem);
        contextMenu.Items.Add(_clickThroughItem);
        contextMenu.Items.Add(_modelStatusItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ToggleVisibilityRequested?.Invoke();
    }

    public void UpdateModelStatus(ServerStatus status, double? ramMb = null)
    {
        string statusText = status switch
        {
            ServerStatus.Ready => ramMb.HasValue ? $"Modello: Pronto ({ramMb.Value:F0} MB)" : "Modello: Pronto",
            ServerStatus.LoadingModel => "Modello: Caricamento...",
            ServerStatus.Starting => "Modello: Avvio in corso...",
            ServerStatus.Stopped => "Modello: Arrestato",
            ServerStatus.Error => "Modello: Errore",
            _ => "Modello: Sconosciuto"
        };

        _modelStatusItem.Text = statusText;
        _notifyIcon.Text = $"OpenAgent - {statusText}";
        UpdateTrayIcon(status);
    }

    private void UpdateTrayIcon(ServerStatus status)
    {
        try
        {
            Color color = status switch
            {
                ServerStatus.Ready => Color.FromArgb(14, 165, 233), // Electric Cyan
                ServerStatus.Starting => Color.FromArgb(245, 158, 11), // Amber
                ServerStatus.LoadingModel => Color.FromArgb(168, 85, 247), // Violet
                ServerStatus.Error => Color.FromArgb(239, 68, 68), // Red
                ServerStatus.Stopped => Color.FromArgb(107, 114, 128), // Slate Gray
                _ => Color.FromArgb(14, 165, 233)
            };

            var oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = CreateOctopusIcon(color);
            oldIcon?.Dispose();
        }
        catch
        {
            // Best effort icon update
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private static Icon CreateOctopusIcon(Color? customBody = null)
    {
        Color bodyCol = customBody ?? Color.FromArgb(14, 165, 233);
        Color shadowCol = Color.FromArgb(bodyCol.A, (int)(bodyCol.R * 0.78), (int)(bodyCol.G * 0.78), (int)(bodyCol.B * 0.78));
        Color darkCol = Color.FromArgb(bodyCol.A, (int)(bodyCol.R * 0.58), (int)(bodyCol.G * 0.58), (int)(bodyCol.B * 0.58));
        Color lightCol = Color.FromArgb(bodyCol.A, (int)(bodyCol.R + (255 - bodyCol.R) * 0.35), (int)(bodyCol.G + (255 - bodyCol.G) * 0.35), (int)(bodyCol.B + (255 - bodyCol.B) * 0.35));
        Color suckerCol = Color.FromArgb(bodyCol.A, (int)(bodyCol.R + (255 - bodyCol.R) * 0.75), (int)(bodyCol.G + (255 - bodyCol.G) * 0.75), (int)(bodyCol.B + (255 - bodyCol.B) * 0.75));

        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(Color.Transparent);

        using var bodyBrush = new SolidBrush(bodyCol);
        using var shadowBrush = new SolidBrush(shadowCol);
        using var darkBrush = new SolidBrush(darkCol);
        using var lightBrush = new SolidBrush(lightCol);
        using var suckerBrush = new SolidBrush(suckerCol);
        using var whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 252));
        using var pupilBrush = new SolidBrush(Color.FromArgb(15, 23, 42));

        const int ox = 4;
        const int oy = 4;

        // Mantle Dome
        g.FillRectangle(bodyBrush, ox + 9, oy + 4, 6, 1);
        g.FillRectangle(bodyBrush, ox + 7, oy + 5, 10, 1);
        g.FillRectangle(bodyBrush, ox + 6, oy + 6, 12, 2);
        g.FillRectangle(bodyBrush, ox + 5, oy + 8, 14, 6);
        g.FillRectangle(bodyBrush, ox + 6, oy + 14, 12, 1);

        // Mantle Highlights & Shadows
        g.FillRectangle(lightBrush, ox + 8, oy + 5, 3, 1);
        g.FillRectangle(lightBrush, ox + 6, oy + 6, 2, 1);
        g.FillRectangle(shadowBrush, ox + 15, oy + 6, 2, 1);
        g.FillRectangle(shadowBrush, ox + 16, oy + 8, 1, 2);

        // Eyebrows
        g.FillRectangle(shadowBrush, ox + 6, oy + 9, 4, 1);
        g.FillRectangle(shadowBrush, ox + 14, oy + 9, 4, 1);

        // Sclera (White eye background)
        g.FillRectangle(whiteBrush, ox + 7, oy + 10, 3, 3);
        g.FillRectangle(whiteBrush, ox + 14, oy + 10, 3, 3);

        // Pupils
        g.FillRectangle(pupilBrush, ox + 8, oy + 11, 2, 2);
        g.FillRectangle(pupilBrush, ox + 14, oy + 11, 2, 2);

        // Twinkle
        g.FillRectangle(lightBrush, ox + 7, oy + 10, 1, 1);
        g.FillRectangle(lightBrush, ox + 16, oy + 10, 1, 1);

        // Siphon
        g.FillRectangle(shadowBrush, ox + 11, oy + 13, 2, 2);
        g.FillRectangle(darkBrush, ox + 11, oy + 14, 2, 1);

        // Tentacle 1: Outer Left
        g.FillRectangle(bodyBrush, ox + 4, oy + 15, 2, 2);
        g.FillRectangle(bodyBrush, ox + 3, oy + 16, 2, 2);
        g.FillRectangle(shadowBrush, ox + 2, oy + 17, 1, 1);
        g.FillRectangle(suckerBrush, ox + 4, oy + 17, 1, 1);

        // Tentacle 2: Outer-Mid Left
        g.FillRectangle(bodyBrush, ox + 6, oy + 15, 2, 3);
        g.FillRectangle(shadowBrush, ox + 5, oy + 18, 2, 1);
        g.FillRectangle(suckerBrush, ox + 7, oy + 16, 1, 1);

        // Tentacle 3: Inner-Mid Left
        g.FillRectangle(bodyBrush, ox + 8, oy + 15, 2, 4);
        g.FillRectangle(darkBrush, ox + 7, oy + 19, 2, 1);
        g.FillRectangle(suckerBrush, ox + 9, oy + 17, 1, 1);

        // Tentacle 4: Center Left (front)
        g.FillRectangle(shadowBrush, ox + 10, oy + 15, 2, 4);
        g.FillRectangle(darkBrush, ox + 10, oy + 19, 2, 1);
        g.FillRectangle(suckerBrush, ox + 10, oy + 16, 1, 1);
        g.FillRectangle(suckerBrush, ox + 11, oy + 18, 1, 1);

        // Tentacle 5: Center Right (front)
        g.FillRectangle(shadowBrush, ox + 12, oy + 15, 2, 4);
        g.FillRectangle(darkBrush, ox + 12, oy + 19, 2, 1);
        g.FillRectangle(suckerBrush, ox + 13, oy + 16, 1, 1);
        g.FillRectangle(suckerBrush, ox + 12, oy + 18, 1, 1);

        // Tentacle 6: Inner-Mid Right
        g.FillRectangle(bodyBrush, ox + 14, oy + 15, 2, 4);
        g.FillRectangle(darkBrush, ox + 15, oy + 19, 2, 1);
        g.FillRectangle(suckerBrush, ox + 14, oy + 17, 1, 1);

        // Tentacle 7: Outer-Mid Right
        g.FillRectangle(bodyBrush, ox + 16, oy + 15, 2, 3);
        g.FillRectangle(shadowBrush, ox + 17, oy + 18, 2, 1);
        g.FillRectangle(suckerBrush, ox + 16, oy + 16, 1, 1);

        // Tentacle 8: Outer Right
        g.FillRectangle(bodyBrush, ox + 18, oy + 15, 2, 2);
        g.FillRectangle(bodyBrush, ox + 19, oy + 16, 2, 2);
        g.FillRectangle(shadowBrush, ox + 21, oy + 17, 1, 1);
        g.FillRectangle(suckerBrush, ox + 19, oy + 17, 1, 1);

        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

using System.Windows;
using Forms = System.Windows.Forms;

namespace Akeno.ColorVision.App.Services;

/// <summary>System tray icon and menu (uses the WinForms NotifyIcon, which ships with the Windows Desktop runtime).</summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly AppHost _host;
    private bool _balloonShown;

    public TrayService(AppHost host)
    {
        _host = host;
        _icon = new Forms.NotifyIcon
        {
            Text = "Akeno ColorVision",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.DoubleClick += (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        BuildMenu();
    }

    public event EventHandler? ShowMainWindowRequested;

    public event EventHandler<string>? NavigateRequested;

    public event EventHandler? ExitRequested;

    public void UpdateTooltip()
    {
        var state = _host.Filter.IsOn ? "Filter on" : "Filter off";
        var text = $"Akeno ColorVision — {state} ({_host.Profiles.Active.Name})";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void ShowRunningInTrayHint()
    {
        if (_balloonShown)
        {
            return;
        }

        _balloonShown = true;
        _icon.ShowBalloonTip(4000, "Akeno ColorVision is still running",
            "The color filter keeps working. Right-click the tray icon for options, or double-click to open the window.", Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        var open = new Forms.ToolStripMenuItem("Open Akeno ColorVision", null, (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold),
        };
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var isOn = _host.Filter.IsOn;
        menu.Items.Add(new Forms.ToolStripMenuItem(isOn ? "Turn filter OFF" : "Turn filter ON", null, (_, _) => _host.ToggleFilterFromUser()));

        var profiles = new Forms.ToolStripMenuItem("Profile");
        foreach (var profile in _host.Profiles.Profiles)
        {
            var id = profile.Id;
            profiles.DropDownItems.Add(new Forms.ToolStripMenuItem(profile.Name, null, (_, _) => _host.Profiles.SetActive(id))
            {
                Checked = profile.Id == _host.Profiles.Active.Id,
            });
        }

        menu.Items.Add(profiles);
        menu.Items.Add(new Forms.ToolStripMenuItem("Identify a color…", null, (_, _) => NavigateRequested?.Invoke(this, "identifier")));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Reset screen colors (emergency)", null, (_, _) => _host.Filter.EmergencyReset()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings", null, (_, _) => NavigateRequested?.Invoke(this, "settings")));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/akeno.ico"));
            if (info?.Stream is { } stream)
            {
                using (stream)
                {
                    return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
                }
            }
        }
        catch (Exception)
        {
            // Fall back to the default application icon below.
        }

        return System.Drawing.SystemIcons.Application;
    }
}

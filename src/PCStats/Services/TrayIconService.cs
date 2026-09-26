using System.Drawing;
using System.Windows.Forms;

namespace PCStats.Services;

/// <summary>System tray presence: the widget hides from Alt+Tab and the taskbar, so this is the way back to it.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly App _app;
    private readonly SettingsStore _store;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleVisible;
    private readonly ToolStripMenuItem _alwaysOnTop;
    private readonly ToolStripMenuItem _clickThrough;

    public TrayIconService(App app, SettingsStore store)
    {
        _app = app;
        _store = store;

        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColors()) };
        menu.BackColor = Color.FromArgb(22, 26, 33);
        menu.ForeColor = Color.FromArgb(243, 244, 246);

        _toggleVisible = new ToolStripMenuItem("Hide widget", null, (_, _) => ToggleVisible());
        _alwaysOnTop = new ToolStripMenuItem("Always on top", null, (_, _) => _store.Update(s => s.AlwaysOnTop = !s.AlwaysOnTop)) { CheckOnClick = false };
        _clickThrough = new ToolStripMenuItem("Click-through", null, (_, _) => _store.Update(s => s.ClickThrough = !s.ClickThrough)) { CheckOnClick = false };

        menu.Items.Add(_toggleVisible);
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => _app.ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_alwaysOnTop);
        menu.Items.Add(_clickThrough);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => _app.Quit()));
        menu.Opening += (_, _) => RefreshMenuState();

        _icon = new NotifyIcon
        {
            Text = "PC Stats",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleVisible();
        };
        _icon.DoubleClick += (_, _) => _app.ShowSettings();
    }

    private void ToggleVisible()
    {
        if (_app.IsWidgetVisible)
            _app.HideToTray();
        else
            _app.ShowWidget();
    }

    private void RefreshMenuState()
    {
        _toggleVisible.Text = _app.IsWidgetVisible ? "Hide widget" : "Show widget";
        _alwaysOnTop.Checked = _store.Current.AlwaysOnTop;
        _clickThrough.Checked = _store.Current.ClickThrough;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && Icon.ExtractAssociatedIcon(path) is { } icon)
                return icon;
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not load tray icon from executable", ex);
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>Colour table so the WinForms menu matches the WPF dark theme.</summary>
    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(22, 26, 33);
        private static readonly Color Hover = Color.FromArgb(42, 47, 58);
        private static readonly Color Line = Color.FromArgb(52, 57, 68);

        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => Line;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}

using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Netvium.Helpers
{
    public sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Window _owner;
        private bool _disposed;

        public TrayService(Window owner)
        {
            _owner = owner;
            _icon = new NotifyIcon
            {
                Visible = false,
                Text = "Netvium",
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!)
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show Netvium", null, (_, _) => ShowMainWindow());
            menu.Items.Add("Exit", null, (_, _) => ExitApplication());
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => ShowMainWindow();
        }

        public void SetVisible(bool visible) => _icon.Visible = visible;

        public void HideToTray()
        {
            _icon.Visible = true;
            _owner.Hide();
            _owner.ShowInTaskbar = false;
        }

        public void ShowMainWindow()
        {
            _owner.Show();
            _owner.ShowInTaskbar = true;
            _owner.WindowState = WindowState.Normal;
            _owner.Activate();
        }

        private void ExitApplication()
        {
            _icon.Visible = false;
            if (_owner is Netvium.MainWindow mainWindow)
                mainWindow.RequestApplicationShutdown();
            else
                Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}

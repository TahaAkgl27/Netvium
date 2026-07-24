using System.Windows;
using System.Windows.Input;
using Netvium.ViewModels;

namespace Netvium.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;
        private bool _closeApproved;
        private bool _closeInProgress;

        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        }

        private void SettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel.SaveCommand.CanExecute(null))
                    _viewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_closeApproved)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            base.OnClosing(e);
            if (_closeInProgress)
                return;

            _closeInProgress = true;
            try
            {
                if (await _viewModel.TryCloseAsync())
                {
                    _closeApproved = true;
                    _ = Dispatcher.BeginInvoke(new System.Action(Close));
                }
            }
            finally
            {
                _closeInProgress = false;
            }
        }
    }
}

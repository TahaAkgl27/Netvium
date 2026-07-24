using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace Netvium.Models
{
    public class ConnectionTreeNode : INotifyPropertyChanged
    {
        public ConnectionTreeNode(string title, string subtitle, ServerInfo? server)
        {
            Title = title;
            Subtitle = subtitle;
            Server = server;
            Children = new ObservableCollection<ConnectionTreeNode>();
        }

        public string Title { get; }
        public string Subtitle { get; }
        public ServerInfo? Server { get; }
        public ObservableCollection<ConnectionTreeNode> Children { get; }

        private System.Windows.Media.Brush _statusBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 48, 73)); // Default dark
        public System.Windows.Media.Brush StatusBrush
        {
            get => _statusBrush;
            set
            {
                _statusBrush = value;
                OnPropertyChanged(nameof(StatusBrush));
            }
        }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value) return;
                _isFavorite = value;
                OnPropertyChanged(nameof(IsFavorite));
            }
        }

        private bool _isActiveDashboard;
        public bool IsActiveDashboard
        {
            get => _isActiveDashboard;
            set
            {
                if (_isActiveDashboard == value) return;
                _isActiveDashboard = value;
                OnPropertyChanged(nameof(IsActiveDashboard));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

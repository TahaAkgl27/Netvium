using System.Windows;

namespace Netvium.Views.Licensing
{
    public partial class PremiumPlusRequiredDialog : Window
    {
        public bool OpenPlansRequested { get; private set; }

        public PremiumPlusRequiredDialog()
        {
            InitializeComponent();
        }

        private void OnViewPlansClick(object sender, RoutedEventArgs e)
        {
            OpenPlansRequested = true;
            DialogResult = true;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public static bool Show(Window owner)
        {
            var dialog = new PremiumPlusRequiredDialog { Owner = owner };
            dialog.ShowDialog();
            return dialog.OpenPlansRequested;
        }
    }
}

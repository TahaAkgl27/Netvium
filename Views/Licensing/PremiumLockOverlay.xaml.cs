using System.Windows;
using System.Windows.Controls;

namespace Netvium.Views.Licensing
{
    public enum PremiumLockMode
    {
        PremiumRequired,
        PremiumPlusExclusive
    }

    public partial class PremiumLockOverlay : System.Windows.Controls.UserControl
    {
        public static readonly RoutedEvent ViewPlansRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(ViewPlansRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PremiumLockOverlay));

        public event RoutedEventHandler ViewPlansRequested
        {
            add => AddHandler(ViewPlansRequestedEvent, value);
            remove => RemoveHandler(ViewPlansRequestedEvent, value);
        }

        public PremiumLockOverlay()
        {
            InitializeComponent();
        }

        public PremiumLockMode LockMode
        {
            get => (PremiumLockMode)GetValue(LockModeProperty);
            set => SetValue(LockModeProperty, value);
        }

        public static readonly DependencyProperty LockModeProperty =
            DependencyProperty.Register(nameof(LockMode), typeof(PremiumLockMode), typeof(PremiumLockOverlay),
                new PropertyMetadata(PremiumLockMode.PremiumRequired, OnLockModeChanged));

        private static void OnLockModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PremiumLockOverlay overlay)
                overlay.ApplyLockMode();
        }

        private void ApplyLockMode()
        {
            if (LockMode == PremiumLockMode.PremiumPlusExclusive)
            {
                LockTitleText.Text = "Premium+ Exclusive";
                LockSubtitleText.Text = "Future AI Assistant features require Premium+";
                LockHintText.Text = "Live Screen Reading · Visual Guidance · AI Mentor";
                ViewPlansButton.Content = "View Premium+ Plans";
            }
            else
            {
                LockTitleText.Text = "Premium Required";
                LockSubtitleText.Text = "Upgrade to unlock AI Smart Analysis";
                LockHintText.Text = "*******  Premium Feature  *******";
                ViewPlansButton.Content = "View Plans";
            }
        }

        private void OnViewPlansClick(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(ViewPlansRequestedEvent, this));
    }
}

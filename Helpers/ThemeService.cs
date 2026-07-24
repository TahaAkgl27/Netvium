using System;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Netvium.Models;

namespace Netvium.Helpers
{
    public static class ThemeService
    {
        private sealed class BorderLayoutState
        {
            public Thickness Padding { get; init; }
        }

        private static readonly ConditionalWeakTable<Border, BorderLayoutState> OriginalCardLayouts = new();

        public static void Apply(Window window, AppSettings settings)
        {
            ApplyAccent(settings.AccentColor);
            ApplyThemeMode(settings.Theme);
            ApplyCompactMode(window, settings.CompactMode);
            ApplyCardRadius(window, settings.CardRadius);
            ApplySidebarWidth(window, settings.SidebarWidth);
            ApplyTooltips(window, settings.ShowTooltips);
            ApplyBlur(window, settings.EnableBlurEffects);

            System.Windows.Application.Current.Resources["NetviumAnimationsEnabled"] = settings.AnimationsEnabled;
            System.Windows.Application.Current.Resources["NetviumStatusAnimationsEnabled"] = settings.ShowStatusAnimations;
        }

        public static void ApplyAccent(string accentHex)
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(accentHex) is not System.Windows.Media.Color accent)
                return;

            System.Windows.Application.Current.Resources["AccentColor"] = accent;
            System.Windows.Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
        }

        public static void ApplyThemeMode(string theme)
        {
            // v1.0 production default: dark theme only (light/system palette is incomplete).
            _ = theme;
            var bg = System.Windows.Media.Color.FromRgb(0x0B, 0x0F, 0x17);
            var card = System.Windows.Media.Color.FromRgb(0x12, 0x18, 0x26);

            System.Windows.Application.Current.Resources["BackgroundColor"] = bg;
            System.Windows.Application.Current.Resources["CardColor"] = card;
            System.Windows.Application.Current.Resources["BackgroundBrush"] = new SolidColorBrush(bg);
            System.Windows.Application.Current.Resources["CardBackgroundBrush"] = new SolidColorBrush(card);
        }

        public static void ApplySidebarWidth(Window window, double width)
        {
            if (window is not MainWindow main || main.LeftSidebarColumn == null)
                return;

            main.LeftSidebarColumn.Width = new GridLength(Math.Clamp(width, 260, 480));
        }

        public static void ApplyTooltips(Window window, bool enabled)
        {
            ToolTipService.SetIsEnabled(window, enabled);
        }

        public static void ApplyCompactMode(Window window, bool compact)
        {
            if (window is not MainWindow main)
                return;

            var margin = compact ? new Thickness(8) : new Thickness(16, 12, 16, 8);
            try
            {
                if (main.FindName("SelectedServerPanel") is FrameworkElement panel)
                    panel.Margin = margin;

                foreach (var border in FindChildren<Border>(main).Where(IsDashboardCard))
                {
                    var original = OriginalCardLayouts.GetValue(border, item => new BorderLayoutState { Padding = item.Padding });
                    border.Padding = compact
                        ? new Thickness(
                            Math.Max(6, original.Padding.Left - 4),
                            Math.Max(6, original.Padding.Top - 4),
                            Math.Max(6, original.Padding.Right - 4),
                            Math.Max(6, original.Padding.Bottom - 4))
                        : original.Padding;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
        }

        public static void ApplyCardRadius(Window window, int radius)
        {
            if (window is not MainWindow main)
                return;

            var r = Math.Clamp(radius, 6, 24);
            foreach (var border in FindChildren<Border>(main))
            {
                if (IsDashboardCard(border) ||
                    border.GetValue(FrameworkElement.NameProperty) is string n && n.Contains("Panel"))
                {
                    border.CornerRadius = new CornerRadius(r);
                }
            }
        }

        public static void ApplyBlur(Window window, bool enabled)
        {
            if (window is not MainWindow main)
                return;

            var shadowColor = System.Windows.Media.Color.FromRgb(0x02, 0x06, 0x17);
            foreach (var border in FindChildren<Border>(main))
            {
                if (enabled && IsDashboardCard(border))
                {
                    border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = shadowColor,
                        BlurRadius = 18,
                        ShadowDepth = 3,
                        Opacity = 0.32
                    };
                }
                else if (border.Effect is System.Windows.Media.Effects.DropShadowEffect)
                    border.Effect = null;
            }
        }

        private static bool IsDashboardCard(Border border)
        {
            var dashPanelCard = System.Windows.Application.Current.TryFindResource("DashPanelCard") as Style;
            return dashPanelCard != null && ReferenceEquals(border.Style, dashPanelCard);
        }

        private static System.Collections.Generic.IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in FindChildren<T>(child))
                    yield return nested;
            }
        }
    }
}

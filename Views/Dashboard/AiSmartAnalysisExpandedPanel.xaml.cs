using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Netvium.Models;
using WpfBinding = System.Windows.Data.Binding;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Netvium.Views.Dashboard
{
    public partial class AiSmartAnalysisExpandedPanel : System.Windows.Controls.UserControl
    {
        private IReadOnlyList<AiSparklineVisual> _pendingSparklines = Array.Empty<AiSparklineVisual>();

        public AiSmartAnalysisExpandedPanel()
        {
            InitializeComponent();
            ApplyItemTemplates();
            Loaded += (_, _) =>
            {
                RenderPendingSparklines();
                RefreshLicensingUi();
            };
            SizeChanged += (_, _) => RenderPendingSparklines();
            PremiumPlusSectionLock.ViewPlansRequested += (_, e) =>
                RaiseEvent(new RoutedEventArgs(ViewPlansRequestedEvent, this));
        }

        public static readonly RoutedEvent ViewPlansRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(ViewPlansRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AiSmartAnalysisExpandedPanel));

        public event RoutedEventHandler ViewPlansRequested
        {
            add => AddHandler(ViewPlansRequestedEvent, value);
            remove => RemoveHandler(ViewPlansRequestedEvent, value);
        }

        public bool ScrollByWheelDelta(int wheelDelta)
        {
            if (ExpandedContentScrollViewer.Visibility != Visibility.Visible
                || ExpandedContentScrollViewer.ScrollableHeight <= 0)
                return false;

            var direction = wheelDelta > 0 ? -1 : 1;
            var step = Math.Max(48, SystemParameters.WheelScrollLines * 24);
            var target = Math.Clamp(
                ExpandedContentScrollViewer.VerticalOffset + direction * step,
                0,
                ExpandedContentScrollViewer.ScrollableHeight);
            if (Math.Abs(target - ExpandedContentScrollViewer.VerticalOffset) < 0.5)
                return false;

            ExpandedContentScrollViewer.ScrollToVerticalOffset(target);
            return true;
        }

        public void RefreshLicensingUi()
        {
            var entitlements = Netvium.Services.FeatureEntitlementService.Instance;
            PremiumPlusSectionLock.Visibility = entitlements.IsPremiumEnabled && !entitlements.IsAiGuideEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public void Bind(AiExpandedWorkspaceState state)
        {
            // §1 Executive Dashboard
            V2HeadlineText.Text = state.Headline;
            V2SubheadlineText.Text = state.Subheadline;
            V2HealthScoreText.Text = state.HealthScoreDisplay;
            AiExpandedVisualBuilder.ApplyRing(V2HealthRing, state.HealthScoreValue, HealthRingColor(state.HealthScoreValue));
            V2RiskLevelText.Text = state.RiskLevel;
            V2RiskBadge.Background = BrushFromHex(BlendBadgeBackground(state.RiskLevelColor));
            V2RiskLevelText.Foreground = BrushFromHex(state.RiskLevelColor);
            V2ConfidenceText.Text = state.AiConfidenceDisplay;
            AiExpandedVisualBuilder.ApplyRing(V2ConfidenceRing, state.AiConfidenceValue, "#4F8CFF");
            V2BusinessImpactText.Text = state.BusinessImpactLevel;
            V2BusinessImpactText.Foreground = BrushFromHex(state.BusinessImpactColor);
            V2BusinessImpactBar.Background = BrushFromHex(state.BusinessImpactColor);
            V2BusinessImpactBar.Width = BusinessImpactBarWidth(state.BusinessImpactLevel);
            V2InfraStatusText.Text = state.InfrastructureStatus;
            V2InfraStatusDot.Fill = BrushFromHex(state.InfrastructureStatusColor);
            V2FixTimeText.Text = state.EstimatedFixTime;
            V2AnalysisAgeText.Text = state.AnalysisAge;
            V2SourceText.Text = state.AnalysisSource;
            V2RoleText.Text = state.ServerRole;

            // §2 Infrastructure map
            V2InfraMapList.ItemsSource = state.InfrastructureMap;

            // §3 Risk distribution
            BindRiskDistribution(state.RiskDistribution);
            V2RiskDetailsList.ItemsSource = state.CriticalRisks
                .Concat(state.HighRisks)
                .Concat(state.MediumRisks)
                .Concat(state.LowRisks)
                .ToList();

            // §4–§11
            V2ChangesList.ItemsSource = state.Changes;
            V2TimelineList.ItemsSource = state.Timeline;
            V2ReasoningList.ItemsSource = state.ReasoningPipeline;
            V2BusinessImpactList.ItemsSource = state.BusinessImpacts;
            V2ActionCenterList.ItemsSource = state.ActionCenter;
            V2PredictionsList.ItemsSource = state.Predictions;
            V2LiveInsightsList.ItemsSource = state.LiveInsights;

            _pendingSparklines = state.TrendSparklines;
            V2SparklinesList.ItemsSource = state.TrendSparklines;
            Dispatcher.BeginInvoke(new Action(RenderPendingSparklines), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void RenderPendingSparklines()
        {
            if (_pendingSparklines.Count == 0 || V2SparklinesList.Items.Count == 0)
                return;

            for (var i = 0; i < V2SparklinesList.Items.Count; i++)
            {
                if (V2SparklinesList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
                    continue;
                if (presenter.Content is not AiSparklineVisual spark)
                    continue;
                var canvas = FindVisualChild<Canvas>(presenter, "SparkCanvas");
                if (canvas != null)
                    AiExpandedVisualBuilder.DrawSparkline(canvas, spark.Points, spark.AccentColor);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name)
                    return fe;
                var found = FindVisualChild<T>(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void BindRiskDistribution(AiRiskDistributionVisual dist)
        {
            V2CriticalCountText.Text = $"Critical {dist.Critical}";
            V2HighCountText.Text = $"High {dist.High}";
            V2MediumCountText.Text = $"Medium {dist.Medium}";
            V2LowCountText.Text = $"Low {dist.Low}";

            V2RiskDistBars.Children.Clear();
            V2RiskDistBars.ColumnDefinitions.Clear();
            var total = Math.Max(1, dist.Total);
            AddDistBar(dist.Critical, total, "#F97316");
            AddDistBar(dist.High, total, "#F59E0B");
            AddDistBar(dist.Medium, total, "#94A3B8");
            AddDistBar(dist.Low, total, "#64748B");
        }

        private void AddDistBar(int count, int total, string color)
        {
            if (count <= 0)
                return;
            var bar = new Border
            {
                Background = BrushFromHex(color),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 2, 0)
            };
            bar.SetValue(Grid.ColumnProperty, V2RiskDistBars.Children.Count);
            var col = new ColumnDefinition { Width = new GridLength(count, GridUnitType.Star) };
            V2RiskDistBars.ColumnDefinitions.Add(col);
            V2RiskDistBars.Children.Add(bar);
        }

        private static string HealthRingColor(int score) =>
            score >= 85 ? "#22C55E" : score >= 60 ? "#F59E0B" : "#F97316";

        private static double BusinessImpactBarWidth(string level) => level switch
        {
            "Critical" => 120,
            "High" => 96,
            "Medium" => 64,
            "Low" => 36,
            _ => 8
        };

        private static string BlendBadgeBackground(string colorHex)
        {
            try
            {
                var c = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}33";
            }
            catch
            {
                return "#1A2A1A";
            }
        }

        private static SolidColorBrush BrushFromHex(string hex) =>
            new((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);

        private void ApplyItemTemplates()
        {
            V2InfraMapList.ItemTemplate = CreateInfraNodeTemplate();
            V2RiskDetailsList.ItemTemplate = CreateRiskTemplate();
            V2ChangesList.ItemTemplate = CreateChangeTemplate();
            V2TimelineList.ItemTemplate = CreateTimelineTemplate();
            V2ReasoningList.ItemTemplate = CreateReasoningTemplate();
            V2BusinessImpactList.ItemTemplate = CreateBusinessImpactTemplate();
            V2ActionCenterList.ItemTemplate = CreateActionCenterTemplate();
            V2PredictionsList.ItemTemplate = CreatePredictionTemplate();
            V2LiveInsightsList.ItemTemplate = CreateLiveInsightTemplate();
            V2SparklinesList.ItemTemplate = CreateSparklineTemplate();
        }

        private static DataTemplate CreateInfraNodeTemplate()
        {
            var template = new DataTemplate(typeof(AiInfraHealthNode));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.WidthProperty, 118.0);
            border.SetValue(Border.MinHeightProperty, 88.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(10));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 8));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiInfraHealthNode.IconGlyph)));
            icon.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            icon.SetValue(TextBlock.FontSizeProperty, 16.0);
            icon.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiInfraHealthNode.StatusColor))
            {
                Converter = new HexToBrushConverter()
            });
            stack.AppendChild(icon);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiInfraHealthNode.Name)));
            name.SetValue(TextBlock.FontSizeProperty, 10.0);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xED, 0xF5)));
            name.SetValue(TextBlock.MarginProperty, new Thickness(0, 6, 0, 4));
            stack.AppendChild(name);

            var statusRow = new FrameworkElementFactory(typeof(StackPanel));
            statusRow.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            var dot = new FrameworkElementFactory(typeof(Ellipse));
            dot.SetValue(FrameworkElement.WidthProperty, 7.0);
            dot.SetValue(FrameworkElement.HeightProperty, 7.0);
            dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0));
            dot.SetBinding(Shape.FillProperty, new WpfBinding(nameof(AiInfraHealthNode.StatusColor))
            {
                Converter = new HexToBrushConverter()
            });
            statusRow.AppendChild(dot);
            var status = new FrameworkElementFactory(typeof(TextBlock));
            status.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiInfraHealthNode.StatusLabel)));
            status.SetValue(TextBlock.FontSizeProperty, 9.0);
            status.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiInfraHealthNode.StatusColor))
            {
                Converter = new HexToBrushConverter()
            });
            statusRow.AppendChild(status);
            stack.AppendChild(statusRow);

            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreateRiskTemplate()
        {
            var template = new DataTemplate(typeof(AiExpandedRiskItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.WidthProperty, 280.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 1));
            border.SetValue(Border.PaddingProperty, new Thickness(12));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 10, 10));
            border.SetBinding(Border.BorderBrushProperty, new WpfBinding(nameof(AiExpandedRiskItem.SeverityColor))
            {
                Converter = new HexToBrushConverter()
            });

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            AddTextBlock(stack, nameof(AiExpandedRiskItem.Title), 12, FontWeights.SemiBold, "#E8EDF5");
            AddTextBlock(stack, nameof(AiExpandedRiskItem.Reason), 10, FontWeights.Normal, "#7B8AA3", maxLines: 2);
            var meta = new FrameworkElementFactory(typeof(StackPanel));
            meta.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
            AddInlineTextBlock(meta, nameof(AiExpandedRiskItem.Confidence), 9, FontWeights.SemiBold, "#4F8CFF", "Conf {0}");
            AddInlineTextBlock(meta, nameof(AiExpandedRiskItem.BusinessImpact), 9, FontWeights.Normal, "#94A3B8", " · {0}");
            stack.AppendChild(meta);
            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreateChangeTemplate()
        {
            var template = new DataTemplate(typeof(AiChangeIndicator));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(10));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 8));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiChangeIndicator.ArrowGlyph)));
            arrow.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            arrow.SetValue(TextBlock.FontSizeProperty, 14.0);
            arrow.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiChangeIndicator.AccentColor))
            {
                Converter = new HexToBrushConverter()
            });
            arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            row.AppendChild(arrow);
            var metric = new FrameworkElementFactory(typeof(TextBlock));
            metric.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiChangeIndicator.Metric)));
            metric.SetValue(TextBlock.FontSizeProperty, 10.0);
            metric.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            metric.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xED, 0xF5)));
            row.AppendChild(metric);
            stack.AppendChild(row);
            AddTextBlock(stack, nameof(AiChangeIndicator.CurrentValue), 16, FontWeights.Bold, "#E8EDF5");
            AddTextBlock(stack, nameof(AiChangeIndicator.DeltaLabel), 9, FontWeights.Normal, "#64748B");
            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreateTimelineTemplate()
        {
            var template = new DataTemplate(typeof(AiTimelineNode));
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 0));
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(56));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(20));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(col0);
            grid.AppendChild(col1);
            grid.AppendChild(col2);

            var time = new FrameworkElementFactory(typeof(TextBlock));
            time.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiTimelineNode.TimeLabel)));
            time.SetValue(TextBlock.FontSizeProperty, 10.0);
            time.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            time.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x94, 0xA3, 0xB8)));
            grid.AppendChild(time);

            var rail = new FrameworkElementFactory(typeof(StackPanel));
            rail.SetValue(Grid.ColumnProperty, 1);
            rail.SetValue(StackPanel.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
            var dot = new FrameworkElementFactory(typeof(Ellipse));
            dot.SetValue(FrameworkElement.WidthProperty, 10.0);
            dot.SetValue(FrameworkElement.HeightProperty, 10.0);
            dot.SetBinding(Shape.FillProperty, new WpfBinding(nameof(AiTimelineNode.SeverityColor))
            {
                Converter = new HexToBrushConverter()
            });
            rail.AppendChild(dot);
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(Border.WidthProperty, 2.0);
            line.SetValue(Border.HeightProperty, 28.0);
            line.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            line.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 0));
            line.SetBinding(UIElement.VisibilityProperty, new WpfBinding(nameof(AiTimelineNode.ShowConnector))
            {
                Converter = new BooleanToVisibilityConverter()
            });
            rail.AppendChild(line);
            grid.AppendChild(rail);

            var content = new FrameworkElementFactory(typeof(StackPanel));
            content.SetValue(Grid.ColumnProperty, 2);
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 12));
            AddTextBlock(content, nameof(AiTimelineNode.Title), 11, FontWeights.SemiBold, "#E8EDF5");
            AddTextBlock(content, nameof(AiTimelineNode.Detail), 10, FontWeights.Normal, "#64748B");
            grid.AppendChild(content);

            template.VisualTree = grid;
            return template;
        }

        private static DataTemplate CreateReasoningTemplate()
        {
            var template = new DataTemplate(typeof(AiReasoningStepVisual));
            var wrap = new FrameworkElementFactory(typeof(StackPanel));
            wrap.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            wrap.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));

            var card = new FrameworkElementFactory(typeof(Border));
            card.SetValue(Border.MinWidthProperty, 120.0);
            card.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            card.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            card.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            card.SetBinding(Border.BackgroundProperty, new WpfBinding(nameof(AiReasoningStepVisual.IsComplete))
            {
                Converter = new ReasoningBackgroundConverter()
            });
            card.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));

            var inner = new FrameworkElementFactory(typeof(StackPanel));
            var iconRow = new FrameworkElementFactory(typeof(StackPanel));
            iconRow.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiReasoningStepVisual.IconGlyph)));
            icon.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            icon.SetValue(TextBlock.FontSizeProperty, 14.0);
            icon.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(0xA7, 0x8B, 0xFA)));
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            iconRow.AppendChild(icon);
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiReasoningStepVisual.Title)));
            title.SetValue(TextBlock.FontSizeProperty, 10.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xED, 0xF5)));
            iconRow.AppendChild(title);
            inner.AppendChild(iconRow);
            AddTextBlock(inner, nameof(AiReasoningStepVisual.Subtitle), 9, FontWeights.Normal, "#64748B");
            card.AppendChild(inner);
            wrap.AppendChild(card);

            template.VisualTree = wrap;
            return template;
        }

        private static DataTemplate CreateBusinessImpactTemplate()
        {
            var template = new DataTemplate(typeof(AiBusinessImpactVisual));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(10));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 8));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            AddTextBlock(stack, nameof(AiBusinessImpactVisual.Area), 10, FontWeights.SemiBold, "#E8EDF5");
            var track = new FrameworkElementFactory(typeof(Border));
            track.SetValue(Border.HeightProperty, 6.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            track.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x2A, 0x3F)));
            track.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 4));
            var fill = new FrameworkElementFactory(typeof(Border));
            fill.SetValue(Border.HeightProperty, 6.0);
            fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            fill.SetValue(Border.HorizontalAlignmentProperty, WpfHorizontalAlignment.Left);
            fill.SetBinding(Border.WidthProperty, new WpfBinding(nameof(AiBusinessImpactVisual.ImpactFill))
            {
                Converter = new ImpactFillWidthConverter()
            });
            fill.SetBinding(Border.BackgroundProperty, new WpfBinding(nameof(AiBusinessImpactVisual.ImpactColor))
            {
                Converter = new HexToBrushConverter()
            });
            track.AppendChild(fill);
            stack.AppendChild(track);
            var level = new FrameworkElementFactory(typeof(TextBlock));
            level.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiBusinessImpactVisual.ImpactLevel)));
            level.SetValue(TextBlock.FontSizeProperty, 9.0);
            level.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiBusinessImpactVisual.ImpactColor))
            {
                Converter = new HexToBrushConverter()
            });
            stack.AppendChild(level);
            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreateActionCenterTemplate()
        {
            var template = new DataTemplate(typeof(AiActionCenterCard));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.WidthProperty, 320.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(14));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 12, 12));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var pri = new FrameworkElementFactory(typeof(Border));
            pri.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            pri.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
            pri.SetValue(Border.HorizontalAlignmentProperty, WpfHorizontalAlignment.Left);
            pri.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 6));
            pri.SetBinding(Border.BackgroundProperty, new WpfBinding(nameof(AiActionCenterCard.PriorityColor))
            {
                Converter = new HexToBadgeBackgroundConverter()
            });
            var priText = new FrameworkElementFactory(typeof(TextBlock));
            priText.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiActionCenterCard.Priority)));
            priText.SetValue(TextBlock.FontSizeProperty, 9.0);
            priText.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            priText.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiActionCenterCard.PriorityColor))
            {
                Converter = new HexToBrushConverter()
            });
            pri.AppendChild(priText);
            stack.AppendChild(pri);
            AddTextBlock(stack, nameof(AiActionCenterCard.Title), 12, FontWeights.SemiBold, "#E8EDF5");
            AddTextBlock(stack, nameof(AiActionCenterCard.ExpectedImprovement), 10, FontWeights.Normal, "#94A3B8", "Improvement: {0}");
            AddTextBlock(stack, nameof(AiActionCenterCard.EstimatedDuration), 10, FontWeights.Normal, "#64748B", "Duration: {0}");
            AddTextBlock(stack, nameof(AiActionCenterCard.RequiredPermissions), 10, FontWeights.Normal, "#64748B", "Permissions: {0}");
            AddTextBlock(stack, nameof(AiActionCenterCard.AutomationNote), 9, FontWeights.Normal, "#64748B");
            AddTextBlock(stack, nameof(AiActionCenterCard.BusinessImpactReduction), 9, FontWeights.SemiBold, "#22C55E", "Impact ↓ {0}");

            var riskRow = new FrameworkElementFactory(typeof(StackPanel));
            riskRow.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            riskRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            AddInlineTextBlock(riskRow, nameof(AiActionCenterCard.RiskBefore), 11, FontWeights.Bold, "#F97316", "Risk {0}");
            var down = new FrameworkElementFactory(typeof(TextBlock));
            down.SetValue(TextBlock.TextProperty, " \u2193 ");
            down.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x22, 0xC5, 0x5E)));
            riskRow.AppendChild(down);
            AddInlineTextBlock(riskRow, nameof(AiActionCenterCard.RiskAfter), 11, FontWeights.Bold, "#22C55E", "{0}");
            stack.AppendChild(riskRow);
            AddTextBlock(stack, nameof(AiActionCenterCard.CommandExample), 9, FontWeights.Normal, "#4F8CFF", "Cmd: {0}");

            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreatePredictionTemplate()
        {
            var template = new DataTemplate(typeof(AiPredictionVisual));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.WidthProperty, 240.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0A, 0x10, 0x20)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(12));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 10, 10));
            border.SetBinding(Border.BorderBrushProperty, new WpfBinding(nameof(AiPredictionVisual.SeverityColor))
            {
                Converter = new HexToBrushConverter()
            });
            border.SetBinding(Border.OpacityProperty, new WpfBinding(nameof(AiPredictionVisual.Available))
            {
                Converter = new AvailableOpacityConverter()
            });

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetValue(TextBlock.TextProperty, "\uE7C5");
            icon.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            icon.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiPredictionVisual.SeverityColor))
            {
                Converter = new HexToBrushConverter()
            });
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            row.AppendChild(icon);
            AddInlineTextBlock(row, nameof(AiPredictionVisual.Title), 11, FontWeights.SemiBold, "#E8EDF5", "{0}");
            stack.AppendChild(row);
            AddTextBlock(stack, nameof(AiPredictionVisual.Detail), 10, FontWeights.Normal, "#64748B");
            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreateLiveInsightTemplate()
        {
            var template = new DataTemplate(typeof(AiLiveInsightCard));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.WidthProperty, 180.0);
            border.SetValue(Border.MinHeightProperty, 96.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(12));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 10, 10));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiLiveInsightCard.IconGlyph)));
            icon.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            icon.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiLiveInsightCard.StatusColor))
            {
                Converter = new HexToBrushConverter()
            });
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            row.AppendChild(icon);
            AddInlineTextBlock(row, nameof(AiLiveInsightCard.Title), 10, FontWeights.SemiBold, "#94A3B8", "{0}");
            stack.AppendChild(row);
            AddTextBlock(stack, nameof(AiLiveInsightCard.Value), 14, FontWeights.Bold, "#E8EDF5");
            var activity = new FrameworkElementFactory(typeof(Border));
            activity.SetValue(Border.HeightProperty, 3.0);
            activity.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            activity.SetValue(Border.MarginProperty, new Thickness(0, 8, 0, 0));
            activity.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x2A, 0x3F)));
            var fill = new FrameworkElementFactory(typeof(Border));
            fill.SetValue(Border.HeightProperty, 3.0);
            fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            fill.SetValue(Border.HorizontalAlignmentProperty, WpfHorizontalAlignment.Left);
            fill.SetBinding(Border.WidthProperty, new WpfBinding(nameof(AiLiveInsightCard.ActivityLevel))
            {
                Converter = new ActivityWidthConverter()
            });
            fill.SetBinding(Border.BackgroundProperty, new WpfBinding(nameof(AiLiveInsightCard.StatusColor))
            {
                Converter = new HexToBrushConverter()
            });
            activity.AppendChild(fill);
            stack.AppendChild(activity);
            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static DataTemplate CreateSparklineTemplate()
        {
            var template = new DataTemplate(typeof(AiSparklineVisual));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0x26, 0x35, 0x4C)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(10));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 8));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var header = new FrameworkElementFactory(typeof(StackPanel));
            header.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
            AddInlineTextBlock(header, nameof(AiSparklineVisual.Label), 10, FontWeights.SemiBold, "#94A3B8", "{0}");
            var trend = new FrameworkElementFactory(typeof(TextBlock));
            trend.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(AiSparklineVisual.TrendLabel)));
            trend.SetValue(TextBlock.FontSizeProperty, 9.0);
            trend.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
            trend.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(AiSparklineVisual.AccentColor))
            {
                Converter = new HexToBrushConverter()
            });
            header.AppendChild(trend);
            stack.AppendChild(header);
            AddTextBlock(stack, nameof(AiSparklineVisual.CurrentValue), 14, FontWeights.Bold, "#E8EDF5");
            var canvas = new FrameworkElementFactory(typeof(Canvas));
            canvas.SetValue(FrameworkElement.NameProperty, "SparkCanvas");
            canvas.SetValue(FrameworkElement.HeightProperty, 28.0);
            canvas.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
            stack.AppendChild(canvas);
            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private static void AddTextBlock(
            FrameworkElementFactory parent,
            string bindingPath,
            double fontSize,
            FontWeight weight,
            string colorHex,
            string? format = null,
            int maxLines = 0)
        {
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetValue(TextBlock.FontSizeProperty, fontSize);
            tb.SetValue(TextBlock.FontWeightProperty, weight);
            tb.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!));
            tb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            tb.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 2));
            if (maxLines > 0)
                tb.SetValue(TextBlock.MaxHeightProperty, maxLines * (fontSize + 4));
            if (format != null)
                tb.SetBinding(TextBlock.TextProperty, new WpfBinding(bindingPath) { StringFormat = format });
            else
                tb.SetBinding(TextBlock.TextProperty, new WpfBinding(bindingPath));
            parent.AppendChild(tb);
        }

        private static void AddInlineTextBlock(
            FrameworkElementFactory parent,
            string bindingPath,
            double fontSize,
            FontWeight weight,
            string colorHex,
            string format)
        {
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetValue(TextBlock.FontSizeProperty, fontSize);
            tb.SetValue(TextBlock.FontWeightProperty, weight);
            tb.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!));
            tb.SetBinding(TextBlock.TextProperty, new WpfBinding(bindingPath) { StringFormat = format });
            parent.AppendChild(tb);
        }

        private sealed class HexToBrushConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is string hex ? BrushFromHex(hex) : System.Windows.Media.Brushes.Gray;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class HexToBadgeBackgroundConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is string hex ? BrushFromHex(BlendBadgeBackground(hex)) : System.Windows.Media.Brushes.Transparent;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class ImpactFillWidthConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is double d ? Math.Max(4, d * 100) : 4.0;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class ActivityWidthConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is double d ? Math.Max(4, d * 160) : 4.0;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class AvailableOpacityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is bool available ? (available ? 1.0 : 0.55) : 0.55;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }

        private sealed class ReasoningBackgroundConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is bool complete && complete
                    ? new SolidColorBrush(WpfColor.FromRgb(0x12, 0x1F, 0x35))
                    : new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x13, 0x22));

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                throw new NotSupportedException();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Netvium.Views.Dashboard
{
    internal static class AiExpandedVisualBuilder
    {
        public static void ApplyRing(Ellipse ring, double percent, string colorHex)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!;
            ring.Stroke = new SolidColorBrush(color);
            var dash = Math.Max(0.08, (Math.Clamp(percent, 0, 100) / 100.0) * 2.6);
            ring.StrokeDashArray = new DoubleCollection { dash, 2.8 };
        }

        public static void DrawSparkline(System.Windows.Controls.Canvas canvas, IReadOnlyList<double> points, string colorHex)
        {
            canvas.Children.Clear();
            if (points == null || points.Count == 0)
                return;

            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!;
            var brush = new SolidColorBrush(color);
            var w = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width;
            var h = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.Height;
            if (w <= 0) w = 120;
            if (h <= 0) h = 28;

            var poly = new Polyline
            {
                Stroke = brush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = System.Windows.Media.Brushes.Transparent
            };

            for (var i = 0; i < points.Count; i++)
            {
                var x = points.Count == 1 ? w / 2 : (w * i / (points.Count - 1));
                var y = h - (Math.Clamp(points[i], 0, 1) * (h - 4)) - 2;
                poly.Points.Add(new System.Windows.Point(x, y));
            }

            canvas.Children.Add(poly);

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = brush
            };
            System.Windows.Controls.Canvas.SetLeft(dot, poly.Points[^1].X - 3);
            System.Windows.Controls.Canvas.SetTop(dot, poly.Points[^1].Y - 3);
            canvas.Children.Add(dot);
        }
    }
}

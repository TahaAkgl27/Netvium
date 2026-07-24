using System.IO;

using System.Windows;

using System.Windows.Media;

using System.Windows.Media.Imaging;

using System.Windows.Threading;

using Netvium.Helpers;

using Netvium.Models.AIGuide;



namespace Netvium.Services.AIGuide

{

    /// <summary>

    /// Captures visible content into memory (PNG). RDP ActiveX uses Win32 screen capture; WPF uses RenderTargetBitmap.

    /// </summary>

    public sealed class ScreenCaptureService

    {

        private readonly RdpScreenCaptureProvider _rdpCapture = new();



        public async Task<ScreenContext> CaptureAsync(ScreenCaptureOptions options, CancellationToken ct = default)

        {

            ct.ThrowIfCancellationRequested();



            var dispatcher = System.Windows.Application.Current?.Dispatcher

                ?? throw new InvalidOperationException("WPF dispatcher is not available.");



            if (options.Source == ScreenCaptureSource.RdpSession)
            {
                AIGuideAnalysisLogger.CaptureStarted();
                return await CaptureRdpSessionAsync(options, ct).ConfigureAwait(false);
            }



            if (dispatcher.CheckAccess())

                return CaptureWpfElement(options, ct);



            return await dispatcher.InvokeAsync(

                () => CaptureWpfElement(options, ct),

                DispatcherPriority.Background);

        }



        private async Task<ScreenContext> CaptureRdpSessionAsync(
            ScreenCaptureOptions options,
            CancellationToken ct)
        {
            var captureId = Guid.NewGuid().ToString("N");
            var result = await _rdpCapture.CaptureWithRetriesAsync(options, ct).ConfigureAwait(false);

            if (!result.Success || result.PngBytes == null)

            {

                var stage = result.FailureStage != AIGuideFailureStage.None
                    ? result.FailureStage
                    : AIGuideFailureStage.BlankCapture;
                var userMessage = result.FailureMessage
                    ?? RdpScreenCaptureProvider.BlankCaptureUserMessage;
                var safeDetail = stage == AIGuideFailureStage.BlankCapture
                    ? RdpScreenCaptureProvider.BlankCaptureSafeDetail
                    : userMessage;

                if (stage == AIGuideFailureStage.BlankCapture)
                {
                    AIGuideAnalysisLogger.CaptureBlank(
                        0,
                        result.Diagnostics?.BlankScore ?? 100,
                        result.Method ?? "CopyFromScreen",
                        result.Diagnostics?.BlankReason ?? "blank");
                }

                return new ScreenContext

                {

                    CaptureId = captureId,

                    Source = options.Source,

                    MonitorIndex = options.MonitorIndex,

                    Width = result.Bounds.Width,

                    Height = result.Bounds.Height,

                    ServerKey = options.ServerKey,

                    ServerName = options.ServerName,

                    CaptureWarning = userMessage,

                    FailureStage = stage,

                    CaptureDiagnostics = result.Diagnostics

                };

            }



            AIGuideAnalysisLogger.CaptureCompleted(
                0,
                result.Bounds.Width,
                result.Bounds.Height,
                result.PngBytes.Length,
                result.Method ?? "CopyFromScreen");

            AppLogger.Debug(

                $"RDP capture via {result.Method} {result.Bounds.Width}x{result.Bounds.Height}",

                category: "AIGuide",

                action: "RdpCapture");



            return new ScreenContext

            {

                CaptureId = captureId,

                Source = options.Source,

                MonitorIndex = options.MonitorIndex,

                Width = result.Bounds.Width,

                Height = result.Bounds.Height,

                ImageBytes = result.PngBytes,

                ServerKey = options.ServerKey,

                ServerName = options.ServerName,

                CaptureDiagnostics = result.Diagnostics

            };

        }



        private static ScreenContext CaptureWpfElement(ScreenCaptureOptions options, CancellationToken ct)

        {

            ct.ThrowIfCancellationRequested();



            var captureId = Guid.NewGuid().ToString("N");

            var element = options.CaptureRoot;



            byte[]? pngBytes = null;

            var width = 0;

            var height = 0;

            var dpiScale = 1.0;



            try

            {

                element.UpdateLayout();



                width = (int)Math.Max(0, element.ActualWidth);

                height = (int)Math.Max(0, element.ActualHeight);



                if (width <= 0 || height <= 0)

                {

                    width = (int)Math.Max(0, element.RenderSize.Width);

                    height = (int)Math.Max(0, element.RenderSize.Height);

                }



                if (width <= 0 || height <= 0)

                {

                    return new ScreenContext

                    {

                        CaptureId = captureId,

                        Source = options.Source,

                        MonitorIndex = options.MonitorIndex,

                        Width = width,

                        Height = height,

                        ServerKey = options.ServerKey,

                        ServerName = options.ServerName,

                        CaptureWarning = "Capture area has no renderable content.",

                        FailureStage = AIGuideFailureStage.Capture

                    };

                }



                var dpi = VisualTreeHelper.GetDpi(element);
                dpiScale = dpi.PixelsPerInchX / 96.0;

                var rtb = new RenderTargetBitmap(

                    width,

                    height,

                    dpi.PixelsPerInchX,

                    dpi.PixelsPerInchY,

                    PixelFormats.Pbgra32);



                rtb.Render(element);



                var encoder = new PngBitmapEncoder();

                encoder.Frames.Add(BitmapFrame.Create(rtb));

                using var ms = new MemoryStream();

                encoder.Save(ms);

                pngBytes = ms.ToArray();

            }

            catch (Exception ex)

            {

                AppLogger.Warning("AI Guide screen capture failed.", ex, category: "AIGuide", action: "Capture");

                return new ScreenContext

                {

                    CaptureId = captureId,

                    Source = options.Source,

                    MonitorIndex = options.MonitorIndex,

                    Width = width,

                    Height = height,

                    ServerKey = options.ServerKey,

                    ServerName = options.ServerName,

                    CaptureWarning = ex.Message,

                    FailureStage = AIGuideFailureStage.Capture

                };

            }



            if (pngBytes == null || pngBytes.Length == 0)

            {

                return new ScreenContext

                {

                    CaptureId = captureId,

                    Source = options.Source,

                    MonitorIndex = options.MonitorIndex,

                    Width = width,

                    Height = height,

                    ServerKey = options.ServerKey,

                    ServerName = options.ServerName,

                    CaptureWarning = "Capture area has no renderable content.",

                    FailureStage = AIGuideFailureStage.Capture

                };

            }

            var darkPercent = CaptureImageAnalyzer.MeasureDarkPercentage(pngBytes);
            var bounds = CaptureBoundsHelper.GetPhysicalScreenRect(element, options.ClipToWindow);
            var diagnostics = new AIGuideCaptureDiagnostics
            {
                Source = options.Source,
                Bounds = bounds == null ? $"{width}x{height}" : $"{bounds.Value.X},{bounds.Value.Y} {bounds.Value.Width}x{bounds.Value.Height}",
                DpiScale = dpiScale,
                Width = width,
                Height = height,
                Bytes = pngBytes.Length,
                DarkPercent = darkPercent,
                CaptureMethod = "RenderTargetBitmap",
                TargetElement = element.GetType().Name,
                AttemptCount = 1,
                UsedParentBounds = false
            };

            AIGuideAnalysisLogger.CaptureCompleted(0, width, height, pngBytes.Length, "RenderTargetBitmap");

            return new ScreenContext

            {

                CaptureId = captureId,

                Source = options.Source,

                MonitorIndex = options.MonitorIndex,

                Width = width,

                Height = height,

                ImageBytes = pngBytes,

                ServerKey = options.ServerKey,

                ServerName = options.ServerName,

                CaptureDiagnostics = diagnostics

            };

        }

    }

}


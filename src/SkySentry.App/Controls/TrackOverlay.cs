using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkySentry.Core.Analysis;

namespace SkySentry.App.Controls;

/// <summary>Draws in source image coordinates, respecting Uniform preview letterboxing.</summary>
public sealed class TrackOverlay : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(AnalysisSnapshot), typeof(TrackOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(OverlayOptions), typeof(TrackOverlay), new FrameworkPropertyMetadata(new OverlayOptions(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(nameof(SelectedId), typeof(string), typeof(TrackOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(nameof(Preview), typeof(BitmapSource), typeof(TrackOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public AnalysisSnapshot? Snapshot { get => (AnalysisSnapshot?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public OverlayOptions Options { get => (OverlayOptions)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string? SelectedId { get => (string?)GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public BitmapSource? Preview { get => (BitmapSource?)GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var snapshot = Snapshot;
        if (snapshot is null || Preview is null || snapshot.FrameWidth != Preview.PixelWidth || snapshot.FrameHeight != Preview.PixelHeight || snapshot.FrameWidth <= 0) return;
        var scale = Math.Min(ActualWidth / snapshot.FrameWidth, ActualHeight / snapshot.FrameHeight);
        if (scale <= 0) return;
        var left = (ActualWidth - snapshot.FrameWidth * scale) / 2; var top = (ActualHeight - snapshot.FrameHeight * scale) / 2;
        Point Map(PixelPoint point) => new(left + point.X * scale, top + point.Y * scale);
        dc.PushClip(new RectangleGeometry(new Rect(left, top, snapshot.FrameWidth * scale, snapshot.FrameHeight * scale)));
        foreach (var track in snapshot.Tracks)
        {
            var selected = track.TrackId == SelectedId;
            var brush = selected ? Brushes.Gold : Brushes.Aquamarine;
            var pen = new Pen(brush, selected ? 2.5 : 1.5);
            if (track.MissedFrames > 0) pen.DashStyle = DashStyles.Dash;
            if (Options.Trajectories && track.Trajectory.Count > 1)
                for (var i = 1; i < track.Trajectory.Count; i++) dc.DrawLine(pen, Map(track.Trajectory[i - 1].Centroid), Map(track.Trajectory[i].Centroid));
            var corner = Map(new(track.Bounds.X, track.Bounds.Y));
            if (Options.Boxes) dc.DrawRectangle(null, pen, new Rect(corner.X, corner.Y, track.Bounds.Width * scale, track.Bounds.Height * scale));
            if (Options.Ids || Options.Speeds)
            {
                var label = (Options.Ids ? "#" + track.TrackId.Split('-')[^1] : "") +
                    (Options.Speeds ? (Options.Ids ? "  " : "") + ViewModels.TrackRowViewModel.Format(track.Measurement?.AngularSpeed, "°/s") : "");
                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var x = Math.Clamp(corner.X, left, Math.Max(left, left + snapshot.FrameWidth * scale - text.Width));
                var y = Math.Max(top, corner.Y - text.Height - 3);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 10, 18, 24)), null, new Rect(x, y, text.Width + 6, text.Height + 2));
                dc.DrawText(text, new Point(x + 3, y));
            }
        }
        if (Options.Diagnostics)
        {
            var text = new FormattedText($"{snapshot.AnalysisFps:F1} analysis FPS · {snapshot.Tracks.Count} tracks · {snapshot.DroppedFrames} skipped · {snapshot.Status}",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(left + 8, top + 8));
        }
        dc.Pop();
    }
}

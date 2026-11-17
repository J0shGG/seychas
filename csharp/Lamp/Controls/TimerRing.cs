using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lamp.Controls;

public sealed class TimerRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(TimerRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(TimerRing),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
        nameof(ArcBrush),
        typeof(Brush),
        typeof(TimerRing),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowBrushProperty = DependencyProperty.Register(
        nameof(GlowBrush),
        typeof(Brush),
        typeof(TimerRing),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ArcBrush
    {
        get => (Brush)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    public Brush GlowBrush
    {
        get => (Brush)GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    public void AnimateTo(double progress)
    {
        var anim = new DoubleAnimation(Math.Clamp(progress, 0, 1), TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(ProgressProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size * 0.45;
        dc.DrawEllipse(null, new Pen(TrackBrush, 1.6), center, radius, radius);

        var amount = Math.Clamp(Progress, 0, 1);
        if (amount <= 0.001) return;

        var glowBrush = GlowBrush.Clone();
        glowBrush.Opacity = 0.4;
        var glow = new Pen(glowBrush, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var arc = new Pen(ArcBrush, 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, glow, Arc(center, radius, amount));
        dc.DrawGeometry(null, arc, Arc(center, radius, amount));
    }

    static Geometry Arc(Point center, double radius, double progress)
    {
        if (progress >= 0.999)
            return new EllipseGeometry(center, radius, radius);

        var start = new Point(center.X, center.Y - radius);
        var angle = 360 * progress - 90;
        var rad = angle * Math.PI / 180;
        var end = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = progress > 0.5,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = true,
        });
        return new PathGeometry([figure]);
    }
}

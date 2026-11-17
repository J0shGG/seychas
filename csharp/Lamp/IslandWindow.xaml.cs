using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lamp.Core;
using Lamp.Services;

namespace Lamp;

public partial class IslandWindow : Window
{
    const double WindowW = 360;
    const double WindowH = 320;
    const double CollapsedW = 292;
    const double CollapsedH = 48;
    const double OpenW = 340;
    const double SideOpenW = 280;
    const double Circle = 48;
    const double AnimMs = 320;

    readonly LampStore _store;
    readonly DispatcherTimer _coverTimer;
    readonly DispatcherTimer _leaveTimer;
    readonly DispatcherTimer _hostSyncTimer;
    readonly IEasingFunction _ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    bool _expanded;
    bool _covered;
    bool _dragging;
    bool _wanted;
    bool _swallowClick;
    bool _pulsing;
    Point _dragStart;
    IslandEdge? _heldEdge;
    int _coverMisses;
    double _lastW;
    double _lastH;
    string _lastMode = "";

    public IslandWindow(LampStore store)
    {
        _store = store;
        InitializeComponent();
        Width = WindowW;
        Height = WindowH;
        _store.Changed += Refresh;
        _coverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _coverTimer.Tick += (_, _) => PollCover();
        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (!_dragging && !Capsule.IsMouseOver) Collapse();
        };
        _hostSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimMs + 20) };
        _hostSyncTimer.Tick += (_, _) =>
        {
            _hostSyncTimer.Stop();
            if (_wanted) PlaceWindow(false);
        };
        Loaded += (_, _) => Native.LiftTopmost(this);
        Closing += (_, e) =>
        {
            if (_wanted)
            {
                e.Cancel = true;
                App.Current.Restore();
            }
        };
        Refresh();
    }

    bool IsSide => _store.State.Settings.IslandEdge is IslandEdge.left or IslandEdge.right;
    bool IsRail => IsSide && !_expanded && !_covered && !_dragging;
    bool IsCircle => _dragging || (_covered && !_expanded);

    public void ShowIsland()
    {
        _wanted = true;
        _expanded = false;
        _covered = Native.FullscreenCover(this);
        _coverMisses = 0;
        PlaceWindow(false);
        ApplyShape(false);
        Native.ShowInactive(this);
        Native.LiftTopmost(this);
        if (!_coverTimer.IsEnabled) _coverTimer.Start();
        Refresh();
    }

    public void HideIsland()
    {
        _wanted = false;
        _expanded = false;
        _dragging = false;
        _coverTimer.Stop();
        _hostSyncTimer.Stop();
        Hide();
    }

    void Refresh()
    {
        if (!_wanted) return;
        Dispatcher.Invoke(() =>
        {
            var s = _store.State;
            var clock = s.Session is null ? "Лампа" : _store.ClockText;
            TimeText.Text = s.Session is null && !_expanded ? "" : clock;
            if (_expanded && s.Session is null) TimeText.Text = "Лампа";
            RailTime.Text = clock;
            LabelText.Text = _store.IslandLabel;
            StatusText.Text = _store.IslandStatus;
            TitleText.Text = _store.CurrentOpenTask()?.Title ?? "";
            TitleText.Visibility = string.IsNullOrEmpty(TitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
            PaceText.Text = _store.IslandPace;
            TodayText.Text = _store.TodayLine;
            IntentText.Text = s.Intention.Trim();
            IntentText.Visibility = string.IsNullOrEmpty(IntentText.Text) ? Visibility.Collapsed : Visibility.Visible;
            DoneButton.Visibility = _store.CurrentOpenTask() is null ? Visibility.Collapsed : Visibility.Visible;

            var hasProgress = s.Session is { Status: not SessionStatus.done };
            ProgressTrack.Visibility = hasProgress && !IsRail && !IsCircle ? Visibility.Visible : Visibility.Collapsed;
            RailProgress.Visibility = hasProgress && IsRail ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgress(_store.Progress, hasProgress);

            PaintDot(s.Session?.Status);
            Pulse(s.Session?.Status == SessionStatus.running);
        });
    }

    void UpdateProgress(double progress, bool hasProgress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (!hasProgress)
        {
            Animate(ProgressFill, WidthProperty, 0, 180);
            Animate(RailProgressFill, HeightProperty, 0, 180);
            return;
        }
        var trackW = ProgressTrack.ActualWidth > 1 ? ProgressTrack.ActualWidth : 220;
        var trackH = RailProgress.ActualHeight > 1 ? RailProgress.ActualHeight : 220;
        Animate(ProgressFill, WidthProperty, trackW * progress, 280);
        Animate(RailProgressFill, HeightProperty, trackH * progress, 280);
    }

    void PaintDot(SessionStatus? status)
    {
        var fill = status switch
        {
            SessionStatus.running => (Color)FindResource("CopperBright"),
            SessionStatus.paused => Colors.Transparent,
            _ => (Color)FindResource("Gold"),
        };
        var stroke = status == SessionStatus.paused ? (Color)FindResource("Gold") : Colors.Transparent;
        Dot.Fill = new SolidColorBrush(fill);
        RailDot.Fill = new SolidColorBrush(fill);
        Dot.Stroke = new SolidColorBrush(stroke);
        RailDot.Stroke = new SolidColorBrush(stroke);
        Dot.StrokeThickness = status == SessionStatus.paused ? 1.5 : 0;
        RailDot.StrokeThickness = Dot.StrokeThickness;
        var size = IsCircle ? 12 : 8;
        Dot.Width = Dot.Height = size;
        RailDot.Width = RailDot.Height = size;
    }

    void Pulse(bool on)
    {
        if (on == _pulsing) return;
        _pulsing = on;
        if (!on)
        {
            DotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RailDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RailDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DotScale.ScaleX = DotScale.ScaleY = 1;
            RailDotScale.ScaleX = RailDotScale.ScaleY = 1;
            return;
        }
        var pulse = new DoubleAnimation(1, 0.68, TimeSpan.FromMilliseconds(800))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        DotScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        DotScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse.Clone());
        RailDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse.Clone());
        RailDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse.Clone());
    }

    (double W, double H) TargetSize()
    {
        if (IsCircle) return (Circle, Circle);
        var edge = _store.State.Settings.IslandEdge;
        if (edge == IslandEdge.top)
        {
            if (!_expanded) return (CollapsedW, CollapsedH);
            return (OpenW, Math.Clamp(OpenHeight(), 120, 260));
        }
        if (!_expanded) return (CollapsedH, CollapsedW);
        return (SideOpenW, Math.Clamp(Math.Max(CollapsedW, OpenHeight()), 168, 300));
    }

    double OpenHeight()
    {
        var h = 124.0;
        if (_store.CurrentOpenTask() is not null) h += 36;
        if (!string.IsNullOrWhiteSpace(_store.State.Intention)) h += 22;
        if (_store.State.Session is { Status: not SessionStatus.done }) h += 16;
        if (_store.CurrentOpenTask() is not null) h += 40;
        return h;
    }

    void ApplyShape(bool animate)
    {
        var (w, h) = TargetSize();
        var mode = $"{(IsCircle ? "c" : IsRail ? "r" : _expanded ? "o" : "t")}:{_store.State.Settings.IslandEdge}:{Math.Round(w)}x{Math.Round(h)}";
        var same = mode == _lastMode;
        _lastMode = mode;

        Capsule.HorizontalAlignment = _store.State.Settings.IslandEdge switch
        {
            IslandEdge.left => System.Windows.HorizontalAlignment.Left,
            IslandEdge.right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Center,
        };
        Capsule.VerticalAlignment = IsSide ? VerticalAlignment.Center : VerticalAlignment.Top;
        RailProgress.HorizontalAlignment = _store.State.Settings.IslandEdge == IslandEdge.right
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Right;
        RailProgress.Margin = _store.State.Settings.IslandEdge == IslandEdge.right
            ? new Thickness(6, 18, 0, 18)
            : new Thickness(0, 18, 6, 18);

        if (!same || !animate)
        {
            if (!animate)
            {
                StopDim();
                Capsule.Width = w;
                Capsule.Height = h;
                _lastW = w;
                _lastH = h;
            }
            else
            {
                var fromW = Capsule.ActualWidth > 1 ? Capsule.ActualWidth : Capsule.Width;
                var fromH = Capsule.ActualHeight > 1 ? Capsule.ActualHeight : Capsule.Height;
                Animate(Capsule, FrameworkElement.WidthProperty, fromW, w, AnimMs);
                Animate(Capsule, FrameworkElement.HeightProperty, fromH, h, AnimMs);
                _lastW = w;
                _lastH = h;
            }
        }

        Capsule.CornerRadius = new CornerRadius(IsCircle || !_expanded ? Math.Min(w, h) / 2 : 22);
        Capsule.BorderThickness = IsCircle ? new Thickness(0) : new Thickness(1);
        AnimateFill(BrushColor(_expanded && !IsCircle ? "IslandOpen" : "IslandFill"), animate);

        var showRail = IsRail || IsCircle;
        var showCard = !IsRail && !IsCircle;
        RailLayout.IsHitTestVisible = IsRail;
        CardLayout.IsHitTestVisible = showCard;
        AnimateOpacity(RailLayout, showRail ? 1 : 0, showRail ? 180 : 90, showRail ? 40 : 0);
        AnimateOpacity(CardLayout, showCard ? 1 : 0, showCard ? 180 : 90, showCard ? 40 : 0);
        RailTime.Visibility = IsRail ? Visibility.Visible : Visibility.Collapsed;
        AnimateOpacity(RailTime, IsRail ? 1 : 0, 140);

        var bodyOn = _expanded && !IsCircle;
        AnimateOpacity(Body, bodyOn ? 1 : 0, 180, bodyOn ? 70 : 0);
        Animate(BodyShift, TranslateTransform.YProperty, bodyOn ? 0 : 10, AnimMs);
        Body.IsHitTestVisible = bodyOn;

        AnimateOpacity(LabelText, !_expanded && showCard ? 1 : 0, 160);
        AnimateOpacity(StatusText, _expanded && showCard && !string.IsNullOrEmpty(StatusText.Text) ? 1 : 0, 180, _expanded ? 50 : 0);
        Animate(TimeText, TextBlock.FontSizeProperty, _expanded ? 28 : 15, AnimMs);
        HeaderBar.Height = _expanded ? 54 : 48;
        ProgressTrack.Margin = _expanded ? new Thickness(16, 0, 16, 12) : new Thickness(18, 0, 18, 6);

        PaintDot(_store.State.Session?.Status);
    }

    Color BrushColor(string key)
    {
        if (FindResource(key) is Color c) return c;
        if (FindResource(key + "Brush") is SolidColorBrush b) return b.Color;
        return Color.FromRgb(0x14, 0x11, 0x0F);
    }

    void AnimateFill(Color to, bool animate)
    {
        if (!animate)
        {
            CapsuleFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
            CapsuleFill.Color = to;
            return;
        }
        CapsuleFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(AnimMs))
        {
            EasingFunction = _ease,
        });
    }

    void AnimateOpacity(UIElement el, double to, double ms, double delay = 0)
    {
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = _ease,
        });
    }

    void Animate(IAnimatable target, DependencyProperty prop, double to, double ms) =>
        Animate(target, prop, null, to, ms);

    void Animate(IAnimatable target, DependencyProperty prop, double? from, double to, double ms)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = _ease };
        if (from is double f) anim.From = f;
        target.BeginAnimation(prop, anim);
    }

    void StopDim()
    {
        Capsule.BeginAnimation(FrameworkElement.WidthProperty, null);
        Capsule.BeginAnimation(FrameworkElement.HeightProperty, null);
    }

    void SyncHostSize()
    {
        var circleHost = IsCircle && CapsuleFitsCircle();
        var w = circleHost ? Circle : WindowW;
        var h = circleHost ? Circle : WindowH;
        if (Math.Abs(Width - w) < 0.5 && Math.Abs(Height - h) < 0.5) return;
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        Width = w;
        Height = h;
    }

    bool CapsuleFitsCircle()
    {
        var capW = Capsule.ActualWidth > 1 ? Capsule.ActualWidth : Capsule.Width;
        var capH = Capsule.ActualHeight > 1 ? Capsule.ActualHeight : Capsule.Height;
        return capW <= Circle + 4 && capH <= Circle + 4;
    }

    void PlaceWindow(bool animate)
    {
        SyncHostSize();
        var area = Native.MonitorAreaDip(this, fullMonitor: _covered && !_dragging);
        var hostW = Width > 1 ? Width : WindowW;
        var hostH = Height > 1 ? Height : WindowH;
        const double margin = 10;
        double x, y;
        switch (_store.State.Settings.IslandEdge)
        {
            case IslandEdge.left:
                x = area.Left + margin;
                y = area.Top + (area.Height - hostH) / 2;
                break;
            case IslandEdge.right:
                x = area.Right - hostW - margin;
                y = area.Top + (area.Height - hostH) / 2;
                break;
            default:
                x = area.Left + (area.Width - hostW) / 2;
                y = area.Top + margin;
                break;
        }
        x = Clamp(x, area.Left, area.Right - hostW);
        y = Clamp(y, area.Top, area.Bottom - hostH);
        if (!animate)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = x;
            Top = y;
            return;
        }
        BeginAnimation(LeftProperty, new DoubleAnimation(Left, x, TimeSpan.FromMilliseconds(280)) { EasingFunction = _ease });
        BeginAnimation(TopProperty, new DoubleAnimation(Top, y, TimeSpan.FromMilliseconds(280)) { EasingFunction = _ease });
    }

    static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(max, Math.Max(min, value));

    void Expand()
    {
        if (_dragging) return;
        _leaveTimer.Stop();
        _hostSyncTimer.Stop();
        if (_expanded) return;
        _expanded = true;
        PlaceWindow(false);
        ApplyShape(true);
        Refresh();
    }

    void Collapse()
    {
        if (_dragging) return;
        if (!_expanded) return;
        _expanded = false;
        ApplyShape(true);
        Refresh();
        if (_covered)
        {
            _hostSyncTimer.Stop();
            _hostSyncTimer.Start();
        }
        else PlaceWindow(false);
    }

    void PollCover()
    {
        if (!_wanted || _dragging) return;
        var cover = Native.FullscreenCover(this);
        if (cover)
        {
            _coverMisses = 0;
            if (_covered)
            {
                Native.LiftTopmost(this);
                PlaceWindow(false);
                return;
            }
            _covered = true;
            _expanded = false;
            StopDim();
            PlaceWindow(false);
            ApplyShape(false);
            Native.LiftTopmost(this);
            Refresh();
            return;
        }
        if (!_covered) return;
        _coverMisses++;
        if (_coverMisses < 2) return;
        _covered = false;
        PlaceWindow(false);
        ApplyShape(true);
        Refresh();
    }

    void Capsule_MouseEnter(object sender, MouseEventArgs e) => Expand();

    void Capsule_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragging) return;
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.Name == "DoneButton") return;
        _dragStart = e.GetPosition(this);
        Capsule.CaptureMouse();
    }

    void Capsule_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Capsule.IsMouseCaptured) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        if (!_dragging)
        {
            if (dx * dx + dy * dy < 64) return;
            _dragging = true;
            _expanded = false;
            _swallowClick = true;
            ApplyShape(false);
            SyncHostSize();
            Refresh();
        }
        var dpi = Native.DpiOf(this);
        var cursor = Native.CursorDip(dpi);
        var work = Native.WorkAreaDip(cursor, dpi);
        var edge = Native.ChooseEdge(cursor, work, _heldEdge);
        _heldEdge = edge;
        const double size = Circle;
        const double margin = 10;
        var x = Math.Clamp(cursor.X - size / 2, work.Left + margin, work.Right - size - margin);
        var y = Math.Clamp(cursor.Y - size / 2, work.Top + margin, work.Bottom - size - margin);
        var dist = edge switch
        {
            IslandEdge.left => Math.Abs(cursor.X - work.Left),
            IslandEdge.right => Math.Abs(cursor.X - work.Right),
            _ => Math.Abs(cursor.Y - work.Top),
        };
        if (dist <= 112)
        {
            if (edge == IslandEdge.left) x = work.Left + margin;
            else if (edge == IslandEdge.right) x = work.Right - size - margin;
            else y = work.Top + margin;
        }
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = x;
        Top = y;
        Capsule.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        Capsule.VerticalAlignment = VerticalAlignment.Top;
    }

    void Capsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Capsule.ReleaseMouseCapture();
        if (_dragging)
        {
            var dpi = Native.DpiOf(this);
            var cursor = Native.CursorDip(dpi);
            var work = Native.WorkAreaDip(cursor, dpi);
            var edge = Native.ChooseEdge(cursor, work, _heldEdge);
            _store.PatchSettings(s => s.IslandEdge = edge);
            _dragging = false;
            _heldEdge = null;
            _covered = Native.FullscreenCover(this);
            PlaceWindow(true);
            ApplyShape(true);
            Refresh();
            Dispatcher.BeginInvoke(() => _swallowClick = false, DispatcherPriority.Background);
            return;
        }
        if (_swallowClick)
        {
            _swallowClick = false;
            return;
        }
        App.Current.Restore();
    }

    void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _store.Dispatch(ActionKind.CompleteCurrent);
    }
}

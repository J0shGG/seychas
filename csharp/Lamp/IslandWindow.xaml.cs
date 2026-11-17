using System.Windows;
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
    readonly CubicEase _ease = new() { EasingMode = EasingMode.EaseOut };

    bool _expanded;
    bool _covered;
    bool _dragging;
    bool _wanted;
    bool _swallowClick;
    Point _dragStart;
    IslandEdge? _heldEdge;
    int _coverMisses;

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

    public void ShowIsland()
    {
        _wanted = true;
        _expanded = false;
        _covered = Native.FullscreenCover(this);
        _coverMisses = 0;
        PlaceWindow();
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
        Hide();
    }

    void Refresh()
    {
        if (!_wanted) return;
        Dispatcher.Invoke(() =>
        {
            var s = _store.State;
            TimeText.Text = s.Session is null ? (_expanded ? "Лампа" : "") : _store.ClockText;
            if (s.Session is null && !_expanded && IsSide) TimeText.Text = "Лампа";
            LabelText.Text = _store.IslandLabel;
            StatusText.Text = _store.IslandStatus;
            TitleText.Text = _store.CurrentOpenTask()?.Title ?? "";
            TitleText.Visibility = string.IsNullOrEmpty(TitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
            PaceText.Text = _store.IslandPace;
            TodayText.Text = _store.TodayLine;
            IntentText.Text = s.Intention.Trim();
            IntentText.Visibility = string.IsNullOrEmpty(IntentText.Text) ? Visibility.Collapsed : Visibility.Visible;
            var task = _store.CurrentOpenTask();
            DoneButton.Visibility = task is null ? Visibility.Collapsed : Visibility.Visible;
            ProgressTrack.Visibility = s.Session is { Status: not SessionStatus.done } ? Visibility.Visible : Visibility.Collapsed;
            ProgressFill.Width = Math.Max(0, (ProgressTrack.ActualWidth <= 0 ? 200 : ProgressTrack.ActualWidth) * _store.Progress);
            Dot.Fill = s.Session?.Status switch
            {
                SessionStatus.running => (Brush)FindResource("CopperBrightBrush"),
                SessionStatus.paused => (Brush)FindResource("MutedBrush"),
                _ => (Brush)FindResource("CopperBrush"),
            };
            if (_expanded && !_dragging && !_covered) ApplyShape(true);
        });
    }

    bool IsSide => _store.State.Settings.IslandEdge is IslandEdge.left or IslandEdge.right;

    (double W, double H) TargetSize()
    {
        if (_dragging || (_covered && !_expanded)) return (Circle, Circle);
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
        var circle = w <= Circle + 1 && h <= Circle + 1;
        Capsule.HorizontalAlignment = _store.State.Settings.IslandEdge switch
        {
            IslandEdge.left => System.Windows.HorizontalAlignment.Left,
            IslandEdge.right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Center,
        };
        Capsule.VerticalAlignment = IsSide ? VerticalAlignment.Center : VerticalAlignment.Top;

        if (!animate)
        {
            Capsule.BeginAnimation(WidthProperty, null);
            Capsule.BeginAnimation(HeightProperty, null);
            Capsule.Width = w;
            Capsule.Height = h;
        }
        else
        {
            AnimateDim(FrameworkElement.WidthProperty, Capsule.ActualWidth > 0 ? Capsule.ActualWidth : Capsule.Width, w);
            AnimateDim(FrameworkElement.HeightProperty, Capsule.ActualHeight > 0 ? Capsule.ActualHeight : Capsule.Height, h);
        }

        Capsule.CornerRadius = new CornerRadius(circle || !_expanded ? Math.Min(w, h) / 2 : 22);
        Capsule.Background = _expanded
            ? (Brush)FindResource("IslandOpenBrush")
            : (Brush)FindResource("IslandFillBrush");
        Capsule.BorderThickness = circle ? new Thickness(0) : new Thickness(1);
        Body.Opacity = _expanded && !circle ? 1 : 0;
        LabelText.Visibility = circle ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Visibility = circle || string.IsNullOrEmpty(StatusText.Text) ? Visibility.Collapsed : Visibility.Visible;
        TimeText.Visibility = circle ? Visibility.Collapsed : Visibility.Visible;
        TopRow.HorizontalAlignment = IsSide && !_expanded && !circle ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left;
        if (IsSide && !_expanded && !circle)
        {
            TopRow.LayoutTransform = new RotateTransform(-90);
            TimeText.FontSize = 13;
        }
        else
        {
            TopRow.LayoutTransform = Transform.Identity;
            TimeText.FontSize = _expanded ? 28 : 18;
        }
    }

    void AnimateDim(DependencyProperty prop, double from, double to)
    {
        Capsule.BeginAnimation(prop, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(AnimMs))
        {
            EasingFunction = _ease,
        });
    }

    void PlaceWindow()
    {
        var dpi = Native.DpiOf(this);
        var cursor = Native.CursorDip(dpi);
        var work = Native.WorkAreaDip(cursor, dpi);
        const double margin = 10;
        double x, y;
        switch (_store.State.Settings.IslandEdge)
        {
            case IslandEdge.left:
                x = work.Left + margin;
                y = work.Top + (work.Height - WindowH) / 2;
                break;
            case IslandEdge.right:
                x = work.Right - WindowW - margin;
                y = work.Top + (work.Height - WindowH) / 2;
                break;
            default:
                x = work.Left + (work.Width - WindowW) / 2;
                y = work.Top + margin;
                break;
        }
        Left = x;
        Top = y;
    }

    void Expand()
    {
        if (_dragging) return;
        _leaveTimer.Stop();
        _expanded = true;
        ApplyShape(true);
        Refresh();
    }

    void Collapse()
    {
        if (_dragging) return;
        _expanded = false;
        ApplyShape(true);
        Refresh();
    }

    void PollCover()
    {
        if (!_wanted || _dragging) return;
        var cover = Native.FullscreenCover(this);
        if (cover)
        {
            _coverMisses = 0;
            if (_covered) return;
            _covered = true;
            _expanded = false;
            Native.LiftTopmost(this);
            ApplyShape(true);
            return;
        }
        if (!_covered) return;
        _coverMisses++;
        if (_coverMisses < 2) return;
        _covered = false;
        ApplyShape(true);
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
        Left = x - (Capsule.Margin.Left);
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
            PlaceWindow();
            ApplyShape(true);
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

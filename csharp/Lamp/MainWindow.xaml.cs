using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lamp.Core;

namespace Lamp;

public partial class MainWindow : Window
{
    static readonly int[] FocusPresets = [15, 25, 50, 90];
    readonly LampStore _store;
    string _journalDay = Format.TodayKey();
    int _journalYear = DateTime.Now.Year;
    int _journalMonth = DateTime.Now.Month;
    OverlayKind _overlay;

    enum OverlayKind { None, Settings, Journal, Finished }

    public MainWindow(LampStore store)
    {
        _store = store;
        InitializeComponent();
        DataContext = _store;
        _store.Changed += () => Dispatcher.Invoke(Refresh);
        NewTaskBox.GotFocus += (_, _) => { if (NewTaskBox.Text == "Добавить задачу") NewTaskBox.Text = ""; };
        ThoughtBox.GotFocus += (_, _) => { if (ThoughtBox.Text == "Мысль унести сюда — и вернуться к делу") ThoughtBox.Text = ""; };
        Loaded += (_, _) =>
        {
            PlaceholderFields();
            Refresh();
        };
    }

    void PlaceholderFields()
    {
        if (string.IsNullOrWhiteSpace(NewTaskBox.Text)) NewTaskBox.Text = "Добавить задачу";
        if (string.IsNullOrWhiteSpace(ThoughtBox.Text)) ThoughtBox.Text = "Мысль унести сюда — и вернуться к делу";
        if (string.IsNullOrWhiteSpace(IntentionBox.Text) && string.IsNullOrWhiteSpace(_store.State.Intention))
            IntentionBox.Tag = "hint";
    }

    void Refresh()
    {
        var s = _store.State;
        Title = _store.WindowTitle;
        Onboarding.Visibility = s.Onboarded ? Visibility.Collapsed : Visibility.Visible;
        DateLabel.Text = _store.DateLabel;
        StreakLabel.Text = _store.StreakLabel;
        OnlineLabel.Text = _store.OnlineLabel ?? "";
        if (IntentionBox.IsFocused == false) IntentionBox.Text = s.Intention;
        ClockLabel.Text = _store.ClockText;
        KindLabel.Text = _store.SessionKindLabel;
        StageTitle.Text = _store.StageTitle;
        StageHint.Text = _store.StageHint;
        Ring.AnimateTo(_store.Progress);
        ResetBtn.Visibility = _store.CanReset ? Visibility.Visible : Visibility.Collapsed;
        CompleteBtn.Visibility = _store.CanCompleteActive ? Visibility.Visible : Visibility.Collapsed;
        DeskVeil.Background = _store.IsFocusLook
            ? new SolidColorBrush(Color.FromArgb(0xB8, 8, 6, 5))
            : new SolidColorBrush(Color.FromArgb(0x99, 12, 9, 7));
        DeskRoot.Background = (Brush)FindResource("DeskBackground");

        BuildPresets();
        BuildTasks();
        BuildThoughts();
        BuildWeek();
        BuildDone();

        var today = _store.Today;
        TodayFocus.Text = Format.FormatMinutes(today.FocusedMs);
        TodaySessions.Text = $"{today.Sessions} {Format.Plural(today.Sessions, "круг", "круга", "кругов")}";
        TodayDone.Text = $"{today.CompletedTasks} {Format.Plural(today.CompletedTasks, "задача", "задачи", "задач")}";

        if (s.Session?.Status == SessionStatus.done && _overlay == OverlayKind.None)
            ShowFinished();
        else if (s.Session?.Status != SessionStatus.done && _overlay == OverlayKind.Finished)
            HideOverlay();
    }

    void BuildPresets()
    {
        Presets.Children.Clear();
        var current = _store.State.Settings.FocusMin;
        var running = _store.IsRunning;
        foreach (var m in FocusPresets)
        {
            var btn = new Button
            {
                Content = $"{m} мин",
                Style = (Style)FindResource(current == m ? "ChipOn" : "Chip"),
                IsEnabled = !running,
                Tag = m,
            };
            btn.Click += (_, _) => _store.PatchSettings(s => s.FocusMin = m);
            Presets.Children.Add(btn);
        }
        var custom = new TextBox
        {
            Width = 56,
            Text = current.ToString(),
            IsEnabled = !running,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8),
        };
        custom.LostFocus += (_, _) => CommitCustomMinutes(custom.Text);
        custom.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitCustomMinutes(custom.Text);
            Keyboard.ClearFocus();
        };
        Presets.Children.Add(custom);
        Presets.Children.Add(new TextBlock
        {
            Text = "мин",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = (Brush)FindResource("MutedBrush"),
        });
    }

    void CommitCustomMinutes(string text)
    {
        if (!int.TryParse(text, out var minutes) || minutes is < 1 or > 180) { Refresh(); return; }
        _store.PatchSettings(s => s.FocusMin = minutes);
    }

    void BuildTasks()
    {
        TaskList.Children.Clear();
        var open = _store.OpenTasks.ToList();
        if (open.Count == 0)
        {
            TaskList.Children.Add(new TextBlock
            {
                Text = "Стол пуст. Напиши, чем займёшься.",
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        foreach (var task in open)
        {
            var slip = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(11, 11, 8, 11),
                Margin = new Thickness(0, 0, 0, 10),
                Background = PaperBrush(),
                BorderBrush = task.Id == _store.State.ActiveTaskId ? (Brush)FindResource("CopperBrush") : Brushes.Transparent,
                BorderThickness = task.Id == _store.State.ActiveTaskId ? new Thickness(3, 0, 0, 0) : new Thickness(0),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new Button
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 4, 0, 0),
                Background = Brushes.Transparent,
                BorderBrush = ColorBrush("#6d5c4c"),
                BorderThickness = new Thickness(1.5),
                Tag = task.Id,
            };
            check.Click += (_, _) => _store.Dispatch(ActionKind.ToggleDone, (string)check.Tag);
            var title = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Tag = task.Id,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = task.Title,
                            FontFamily = (FontFamily)FindResource("Serif"),
                            FontSize = 18,
                            Foreground = ColorBrush("#1c1612"),
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock
                        {
                            Text = task.FocusedMs > 0 ? $"{Format.FormatMinutes(task.FocusedMs)} в фокусе" : "",
                            FontSize = 11,
                            Foreground = ColorBrush("#6d5c4c"),
                            Visibility = task.FocusedMs > 0 ? Visibility.Visible : Visibility.Collapsed,
                        },
                    },
                },
            };
            title.Click += (_, _) => _store.Dispatch(ActionKind.SelectTask, (string)title.Tag);
            var remove = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = ColorBrush("#8a7360"),
                Tag = task.Id,
                FontSize = 16,
            };
            remove.Click += (_, _) => _store.Dispatch(ActionKind.RemoveTask, (string)remove.Tag);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(check);
            grid.Children.Add(title);
            grid.Children.Add(remove);
            slip.Child = grid;
            TaskList.Children.Add(slip);
        }
    }

    void BuildThoughts()
    {
        ThoughtList.Children.Add(new UIElement()); // keep compiler happy if empty
        ThoughtList.Children.Clear();
        foreach (var thought in _store.VisibleThoughts)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var dismiss = new Button { Content = "убрать", Style = (Style)FindResource("Ghost"), Padding = new Thickness(8, 2, 8, 2), Tag = thought.Id, FontSize = 11 };
            var promote = new Button { Content = "в задачи", Style = (Style)FindResource("Ghost"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), Tag = thought.Id, FontSize = 11 };
            dismiss.Click += (_, _) => _store.Dispatch(ActionKind.DismissThought, (string)dismiss.Tag);
            promote.Click += (_, _) => _store.Dispatch(ActionKind.PromoteThought, (string)promote.Tag);
            DockPanel.SetDock(dismiss, Dock.Right);
            DockPanel.SetDock(promote, Dock.Right);
            row.Children.Add(dismiss);
            row.Children.Add(promote);
            row.Children.Add(new TextBlock
            {
                Text = thought.Text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("PaperBrush"),
            });
            ThoughtList.Children.Add(row);
        }
    }

    void BuildWeek()
    {
        WeekStrip.Children.Clear();
        var keys = Format.WeekKeys();
        var byDate = _store.State.History.ToDictionary(d => d.Date);
        long weekMs = 0;
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            var ms = (byDate.TryGetValue(key, out var d) ? d.FocusedMs : 0) + (key == Format.TodayKey() ? _store.LiveFocusMs : 0);
            weekMs += ms;
            var heat = Format.HeatLevel(ms);
            var cell = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Format.Weekdays[i], HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontSize = 11 },
                        new TextBlock { Text = Format.FormatCompact(ms) is { Length: > 0 } t ? t : "·", HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontSize = 11 },
                    },
                },
                Background = HeatBrush(heat),
                BorderThickness = key == Format.TodayKey() ? new Thickness(1) : new Thickness(0),
                BorderBrush = (Brush)FindResource("CopperBrush"),
                Margin = new Thickness(2),
                Padding = new Thickness(2, 6, 2, 6),
                Foreground = (Brush)FindResource("PaperBrush"),
                Tag = key,
            };
            cell.Click += (_, _) => { _journalDay = (string)cell.Tag; ShowJournal(); };
            WeekStrip.Children.Add(cell);
        }
        WeekTotal.Text = Format.FormatMinutes(weekMs);
    }

    void BuildDone()
    {
        DoneList.Children.Clear();
        foreach (var task in _store.DoneTasks)
        {
            DoneList.Children.Add(new TextBlock
            {
                Text = $"✓ {task.Title}",
                Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    static Brush PaperBrush()
    {
        try
        {
            return new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Assets/paper.jpg")))
            {
                Stretch = Stretch.UniformToFill,
            };
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0xF3, 0xEA, 0xD8));
        }
    }

    static SolidColorBrush ColorBrush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;

    static Brush HeatBrush(int level) => level switch
    {
        1 => new SolidColorBrush(Color.FromArgb(0x55, 196, 120, 74)),
        2 => new SolidColorBrush(Color.FromArgb(0x88, 196, 120, 74)),
        3 => new SolidColorBrush(Color.FromArgb(0xBB, 196, 120, 74)),
        4 => new SolidColorBrush(Color.FromRgb(196, 120, 74)),
        _ => new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
    };

    void AddTask_Click(object sender, RoutedEventArgs e) => AddTask();

    void AddTask()
    {
        var text = NewTaskBox.Text.Trim();
        if (text is "" or "Добавить задачу") return;
        _store.Dispatch(ActionKind.AddTask, text);
        NewTaskBox.Text = "Добавить задачу";
    }

    void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddTask();
        e.Handled = true;
    }

    void ThoughtBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var text = ThoughtBox.Text.Trim();
        if (text is not ("" or "Мысль унести сюда — и вернуться к делу"))
            _store.Dispatch(ActionKind.AddThought, text);
        ThoughtBox.Text = "Мысль унести сюда — и вернуться к делу";
        e.Handled = true;
    }

    void IntentionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IntentionBox.IsFocused)
            _store.Dispatch(ActionKind.SetIntention, IntentionBox.Text);
    }

    void Ring_Click(object sender, RoutedEventArgs e)
    {
        var session = _store.State.Session;
        if (session is null) _store.Dispatch(ActionKind.StartFocus);
        else if (session.Status == SessionStatus.running) _store.Dispatch(ActionKind.Pause);
        else if (session.Status == SessionStatus.paused) _store.Dispatch(ActionKind.Resume);
    }

    void Reset_Click(object sender, RoutedEventArgs e) => _store.Dispatch(ActionKind.Stop);

    void CompleteActive_Click(object sender, RoutedEventArgs e)
    {
        if (_store.State.ActiveTaskId is { } id)
            _store.Dispatch(ActionKind.ToggleDone, id);
    }

    void Onboard_Click(object sender, RoutedEventArgs e) => _store.Dispatch(ActionKind.CompleteOnboarding);

    void OpenSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    void OpenJournal_Click(object sender, RoutedEventArgs e)
    {
        _journalDay = Format.TodayKey();
        ShowJournal();
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Overlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (_overlay == OverlayKind.Finished) return;
        HideOverlay();
    }

    void OverlayCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void HideOverlay()
    {
        _overlay = OverlayKind.None;
        Overlay.Visibility = Visibility.Collapsed;
        OverlayBody.Children.Clear();
    }

    void ShowOverlay(OverlayKind kind)
    {
        _overlay = kind;
        Overlay.Visibility = Visibility.Visible;
        OverlayBody.Children.Clear();
    }

    void ShowSettings()
    {
        ShowOverlay(OverlayKind.Settings);
        OverlayCard.Width = 460;
        OverlayBody.Children.Add(Head("Настройки"));
        OverlayBody.Children.Add(Label("Тема"));
        OverlayBody.Children.Add(ChipRow(
            [("lamp", "Лампа"), ("light", "Светлая"), ("dark", "Тёмная"), ("ink", "Чернила")],
            id => _store.State.Settings.Theme.ToString() == id,
            id =>
            {
                _store.PatchSettings(s => s.Theme = Enum.Parse<ThemeId>(id));
                ShowSettings();
            }));
        OverlayBody.Children.Add(Label("Длина круга"));
        OverlayBody.Children.Add(ChipRow(
            FocusPresets.Select(m => (m.ToString(), m.ToString())),
            id => _store.State.Settings.FocusMin.ToString() == id,
            id =>
            {
                _store.PatchSettings(s => s.FocusMin = int.Parse(id));
                ShowSettings();
            }));
        OverlayBody.Children.Add(Label("Фон"));
        OverlayBody.Children.Add(ChipRow(
            [("off", "Тишина"), ("rain", "Дождь"), ("fire", "Огонь"), ("cafe", "Кафе")],
            id => _store.State.Settings.Ambient.ToString() == id,
            id =>
            {
                _store.PatchSettings(s => s.Ambient = Enum.Parse<AmbientKind>(id));
                ShowSettings();
            }));
        OverlayBody.Children.Add(Label("Громкость"));
        var vol = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = _store.State.Settings.Volume * 100,
            Margin = new Thickness(0, 0, 0, 12),
        };
        vol.ValueChanged += (_, _) => _store.PatchSettings(s => s.Volume = vol.Value / 100);
        OverlayBody.Children.Add(vol);
        OverlayBody.Children.Add(Toggle("Колокол в конце", _store.State.Settings.Chime, v =>
        {
            _store.PatchSettings(s => s.Chime = v);
            ShowSettings();
        }));
        OverlayBody.Children.Add(Toggle("Уведомление системы", _store.State.Settings.Notify, v =>
        {
            _store.PatchSettings(s => s.Notify = v);
            ShowSettings();
        }));
        var close = new Button { Content = "Закрыть", Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 12, 0, 8) };
        close.Click += (_, _) => HideOverlay();
        OverlayBody.Children.Add(close);
        OverlayBody.Children.Add(new TextBlock
        {
            Text = "by J0shGG",
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    void ShowJournal()
    {
        ShowOverlay(OverlayKind.Journal);
        OverlayCard.Width = 520;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock
        {
            Text = "Дни работы",
            FontFamily = (FontFamily)FindResource("Serif"),
            FontSize = 26,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        DockPanel.SetDock(nav, Dock.Right);
        var prev = new Button { Content = "‹", Style = (Style)FindResource("IconBtn") };
        var next = new Button { Content = "›", Style = (Style)FindResource("IconBtn"), Margin = new Thickness(8, 0, 0, 0) };
        var title = new TextBlock
        {
            Text = Format.FormatMonthTitle(_journalYear, _journalMonth),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            MinWidth = 140,
            TextAlignment = TextAlignment.Center,
        };
        prev.Click += (_, _) =>
        {
            var d = new DateTime(_journalYear, _journalMonth, 1).AddMonths(-1);
            _journalYear = d.Year;
            _journalMonth = d.Month;
            ShowJournal();
        };
        next.Click += (_, _) =>
        {
            var d = new DateTime(_journalYear, _journalMonth, 1).AddMonths(1);
            if (d > DateTime.Now) return;
            _journalYear = d.Year;
            _journalMonth = d.Month;
            ShowJournal();
        };
        nav.Children.Add(prev);
        nav.Children.Add(title);
        nav.Children.Add(next);
        head.Children.Add(nav);
        OverlayBody.Children.Add(head);

        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var d in Format.Weekdays)
            weekdays.Children.Add(new TextBlock { Text = d, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Foreground = (Brush)FindResource("MutedBrush") });
        OverlayBody.Children.Add(weekdays);

        var grid = new UniformGrid { Columns = 7 };
        var byDate = _store.State.History.ToDictionary(d => d.Date);
        foreach (var key in Format.MonthCells(_journalYear, _journalMonth))
        {
            if (key is null)
            {
                grid.Children.Add(new Border());
                continue;
            }
            var ms = (byDate.TryGetValue(key, out var day) ? day.FocusedMs : 0) + (key == Format.TodayKey() ? _store.LiveFocusMs : 0);
            var cell = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = int.Parse(key[^2..]).ToString(), HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
                        new TextBlock { Text = Format.FormatCompact(ms), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontSize = 10 },
                    },
                },
                Background = HeatBrush(Format.HeatLevel(ms)),
                IsEnabled = key.CompareTo(Format.TodayKey()) <= 0,
                Tag = key,
                Margin = new Thickness(2),
                Foreground = (Brush)FindResource("PaperBrush"),
                BorderBrush = key == _journalDay ? (Brush)FindResource("CopperBrightBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(1),
            };
            cell.Click += (_, _) => { _journalDay = (string)cell.Tag; ShowJournal(); };
            grid.Children.Add(cell);
        }
        OverlayBody.Children.Add(grid);

        var selected = byDate.TryGetValue(_journalDay, out var picked) ? picked : new DayStat { Date = _journalDay };
        var dayMs = selected.FocusedMs + (_journalDay == Format.TodayKey() ? _store.LiveFocusMs : 0);
        OverlayBody.Children.Add(new TextBlock
        {
            Text = Format.FormatDayHeading(_journalDay),
            FontFamily = (FontFamily)FindResource("Serif"),
            FontSize = 20,
            Margin = new Thickness(0, 16, 0, 4),
        });
        OverlayBody.Children.Add(new TextBlock
        {
            Text = dayMs > 0 || selected.Sessions > 0
                ? $"{Format.FormatMinutes(dayMs)} · {selected.Sessions} {Format.Plural(selected.Sessions, "круг", "круга", "кругов")}"
                : "В этот день лампа ещё не горела.",
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 12),
        });
        var back = new Button { Content = "К столу", Style = (Style)FindResource("Primary") };
        back.Click += (_, _) => HideOverlay();
        OverlayBody.Children.Add(back);
    }

    void ShowFinished()
    {
        ShowOverlay(OverlayKind.Finished);
        OverlayCard.Width = 420;
        var focusEnded = _store.State.Session?.Kind == SessionKind.focus;
        var minutes = StateReducer.BreakMinutes(_store.State);
        OverlayBody.Children.Add(Head(focusEnded ? "Свет погас" : "Пора к делу"));
        OverlayBody.Children.Add(new TextBlock
        {
            Text = focusEnded
                ? $"Круг закрыт. Можно отдохнуть {minutes} мин или зажечь лампу снова."
                : "Перерыв закончился. Вернись к одной задаче.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        var actions = new WrapPanel();
        if (focusEnded)
        {
            actions.Children.Add(ActionButton($"Отдохнуть {minutes} мин", true, () =>
            {
                _store.Dispatch(ActionKind.AckDone);
                _store.Dispatch(ActionKind.StartBreak);
                HideOverlay();
            }));
            actions.Children.Add(ActionButton("Ещё круг", false, () =>
            {
                _store.Dispatch(ActionKind.AckDone);
                _store.Dispatch(ActionKind.StartFocus);
                HideOverlay();
            }));
        }
        else
        {
            actions.Children.Add(ActionButton("Зажечь лампу", true, () =>
            {
                _store.Dispatch(ActionKind.AckDone);
                _store.Dispatch(ActionKind.StartFocus);
                HideOverlay();
            }));
        }
        actions.Children.Add(ActionButton("К столу", false, () =>
        {
            _store.Dispatch(ActionKind.AckDone);
            HideOverlay();
        }));
        OverlayBody.Children.Add(actions);
    }

    Button ActionButton(string text, bool primary, Action click)
    {
        var btn = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "Primary" : "Ghost"),
            Margin = new Thickness(0, 0, 8, 8),
        };
        btn.Click += (_, _) => click();
        return btn;
    }

    TextBlock Head(string text) => new()
    {
        Text = text,
        FontFamily = (FontFamily)FindResource("Serif"),
        FontSize = 28,
        Margin = new Thickness(0, 0, 0, 12),
    };

    TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("MutedBrush"),
        Margin = new Thickness(0, 8, 0, 6),
    };

    WrapPanel ChipRow(IEnumerable<(string Id, string Label)> items, Func<string, bool> on, Action<string> click)
    {
        var row = new WrapPanel();
        foreach (var (id, label) in items)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource(on(id) ? "ChipOn" : "Chip"),
                Tag = id,
            };
            btn.Click += (_, _) => click((string)btn.Tag);
            row.Children.Add(btn);
        }
        return row;
    }

    DockPanel Toggle(string label, bool value, Action<bool> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var btn = new Button
        {
            Content = value ? "вкл" : "выкл",
            Style = (Style)FindResource(value ? "ChipOn" : "Chip"),
        };
        btn.Click += (_, _) => set(!value);
        DockPanel.SetDock(btn, Dock.Right);
        row.Children.Add(btn);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat) return;
        var typing = Keyboard.FocusedElement is TextBox;

        if (e.Key == Key.Escape)
        {
            if (_overlay is OverlayKind.Settings or OverlayKind.Journal)
            {
                HideOverlay();
                e.Handled = true;
                return;
            }
            if (_store.State.Session is not null) _store.Dispatch(ActionKind.Stop);
            e.Handled = true;
            return;
        }
        if (typing) return;
        if (e.Key == Key.Space)
        {
            Ring_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _store.State.ActiveTaskId is { } id)
        {
            _store.Dispatch(ActionKind.ToggleDone, id);
            e.Handled = true;
        }
        else if (e.Key is Key.N)
        {
            NewTaskBox.Focus();
            NewTaskBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key is Key.F)
        {
            Fullscreen_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}

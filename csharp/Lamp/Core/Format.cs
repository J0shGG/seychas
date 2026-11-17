using System.Globalization;

namespace Lamp.Core;

public static class Format
{
    public static readonly string[] Weekdays = ["пн", "вт", "ср", "чт", "пт", "сб", "вс"];

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static string Pad(int n) => n.ToString("00", CultureInfo.InvariantCulture);

    public static string TodayKey(DateTime? d = null)
    {
        var x = d ?? DateTime.Now;
        return $"{x.Year}-{Pad(x.Month)}-{Pad(x.Day)}";
    }

    public static string YesterdayKey(DateTime? d = null)
    {
        var x = (d ?? DateTime.Now).Date.AddDays(-1);
        return TodayKey(x);
    }

    public static string FormatClock(long ms)
    {
        var total = Math.Max(0, (long)Math.Ceiling(ms / 1000.0));
        var m = total / 60;
        var s = total % 60;
        return $"{Pad((int)m)}:{Pad((int)s)}";
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var n10 = n % 10;
        var n100 = n % 100;
        if (n10 == 1 && n100 != 11) return one;
        if (n10 is >= 2 and <= 4 && n100 is < 12 or > 14) return few;
        return many;
    }

    public static string FormatMinutes(long ms)
    {
        var m = (int)Math.Round(ms / 60000.0);
        if (ms <= 0) return $"0 {Plural(0, "минута", "минуты", "минут")}";
        if (m < 1) return "меньше минуты";
        if (m < 60) return $"{m} {Plural(m, "минута", "минуты", "минут")}";
        var h = m / 60;
        var rm = m % 60;
        var hours = $"{h} {Plural(h, "час", "часа", "часов")}";
        return rm == 0 ? hours : $"{hours} {rm} {Plural(rm, "минута", "минуты", "минут")}";
    }

    public static string FormatDateRu(DateTime? d = null) =>
        (d ?? DateTime.Now).ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("ru-RU"));

    public static string Uid() => Guid.NewGuid().ToString("D");

    public static DateTime ParseKey(string key)
    {
        var parts = key.Split('-');
        return new DateTime(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    public static string FormatCompact(long ms)
    {
        var m = (int)Math.Round(ms / 60000.0);
        if (m < 1) return "";
        if (m < 60) return $"{m}м";
        var h = m / 60;
        var rm = m % 60;
        return rm == 0 ? $"{h}ч" : $"{h}ч {rm}м";
    }

    public static string FormatDayHeading(string key)
    {
        if (key == TodayKey()) return "Сегодня";
        if (key == YesterdayKey()) return "Вчера";
        return ParseKey(key).ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("ru-RU"));
    }

    public static string FormatMonthTitle(int year, int month)
    {
        var raw = new DateTime(year, month, 1).ToString("MMMM", CultureInfo.GetCultureInfo("ru-RU")).TrimEnd('.');
        return $"{char.ToUpper(raw[0], CultureInfo.GetCultureInfo("ru-RU"))}{raw[1..]} {year}";
    }

    public static DateTime StartOfWeek(DateTime? d = null)
    {
        var x = (d ?? DateTime.Now).Date;
        var diff = x.DayOfWeek == DayOfWeek.Sunday ? -6 : 1 - (int)x.DayOfWeek;
        return x.AddDays(diff);
    }

    public static string[] WeekKeys(DateTime? d = null)
    {
        var start = StartOfWeek(d);
        return Enumerable.Range(0, 7).Select(i => TodayKey(start.AddDays(i))).ToArray();
    }

    public static string?[] MonthCells(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var days = DateTime.DaysInMonth(year, month);
        var mondayIndex = first.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)first.DayOfWeek - 1;
        var cells = new List<string?>(Enumerable.Repeat<string?>(null, mondayIndex));
        for (var day = 1; day <= days; day++) cells.Add(TodayKey(new DateTime(year, month, day)));
        while (cells.Count % 7 != 0) cells.Add(null);
        return [.. cells];
    }

    public static int HeatLevel(long ms)
    {
        var m = ms / 60000.0;
        if (m <= 0) return 0;
        if (m < 25) return 1;
        if (m < 50) return 2;
        if (m < 100) return 3;
        return 4;
    }
}

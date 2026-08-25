using System.Globalization;
using System.Text;

namespace MasjidApp.Core.PrayerTimes;

// FR-PT-04, API-03, NFR-RL-01: tolerant CSV parser - malformed rows are silently skipped.

/// <summary>
/// Parses plain-text CSV content from the masjid's daily prayer-times feed into a
/// list of <see cref="CsvPrayerTimesRow"/> values.
/// </summary>
/// <remarks>
/// Two layouts are supported, both tolerantly:
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Header-driven</b> (preferred): when the first non-empty line is recognised as a
///     header, columns are mapped by name. Recognised aliases include
///     <c>day</c>/<c>date</c>, <c>fajr_athan</c>/<c>fajr</c>, <c>fajr_iqama</c>,
///     <c>sunrise</c>, <c>dhuhr_athan</c>/<c>dhuhr</c>, <c>dhuhr_iqama</c>,
///     <c>asr_athan</c>/<c>asr</c>, <c>asr_iqama</c>, <c>maghrib_athan</c>/<c>maghrib</c>,
///     <c>maghrib_iqama</c>, <c>isha_athan</c>/<c>isha</c>, and <c>isha_iqama</c>.
///     This matches the SalahAPI-style feed at <c>mock/prayer-times.csv</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Positional</b> (header-less fallback): a 12+ column row is read as
///     <c>date, fajr_athan, fajr_iqama, sunrise, dhuhr_athan, dhuhr_iqama, asr_athan,
///     asr_iqama, maghrib_athan, maghrib_iqama, isha_athan, isha_iqama</c>; a 7–11 column
///     row is read as athan-only <c>date, fajr, sunrise, dhuhr, asr, maghrib, isha</c>.
///     </description>
///   </item>
/// </list>
/// Fields may be double-quoted, and times may include seconds (<c>HH:mm:ss</c>). Rows with
/// too few columns, unparseable dates, or unparseable athan times are silently skipped -
/// the rest of the file is still processed (NFR-RL-01). Iqama times are optional.
/// </remarks>
public static class CsvPrayerTimesParser
{
    private const int MinAthanOnlyColumns = 7;
    private const int FullLayoutColumns = 12;

    /// <summary>Sentinel meaning "this field is not present in the layout".</summary>
    private const int Absent = -1;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "MM/dd/yyyy",
        "dd/MM/yyyy",
        "yyyy/MM/dd",
    ];

    private static readonly string[] TimeFormats =
    [
        "HH:mm:ss",
        "H:mm:ss",
        "HH:mm",
        "H:mm",
        "hh:mm:ss tt",
        "h:mm:ss tt",
        "hh:mm tt",
        "h:mm tt",
    ];

    /// <summary>
    /// Parses <paramref name="csvContent"/> and returns all successfully parsed rows.
    /// Malformed rows are skipped without throwing.
    /// </summary>
    /// <param name="csvContent">Raw CSV text from the prayer-times feed.</param>
    /// <returns>
    /// A read-only list of parsed rows, possibly empty.  Never <see langword="null"/>.
    /// </returns>
    public static IReadOnlyList<CsvPrayerTimesRow> Parse(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return [];
        }

        var rows = new List<CsvPrayerTimesRow>();
        var lines = csvContent.Split('\n');
        ColumnMap? map = null;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var cells = SplitCsvLine(rawLine);

            // Detect and consume a header row once, before any data has been read.
            if (map is null && TryBuildHeaderMap(cells, out var headerMap))
            {
                map = headerMap;
                continue;
            }

            var layout = map ?? PositionalLayout(cells.Count);
            if (layout is null)
            {
                continue;
            }

            if (TryParseRow(cells, layout, out var row))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static bool TryParseRow(List<string> cells, ColumnMap map, out CsvPrayerTimesRow row)
    {
        row = default!;

        if (!TryGetDate(cells, map.Date, out var date) ||
            !TryGetTime(cells, map.FajrAthan, out var fajr) ||
            !TryGetTime(cells, map.Sunrise, out var sunrise) ||
            !TryGetTime(cells, map.DhuhrAthan, out var dhuhr) ||
            !TryGetTime(cells, map.AsrAthan, out var asr) ||
            !TryGetTime(cells, map.MaghribAthan, out var maghrib) ||
            !TryGetTime(cells, map.IshaAthan, out var isha))
        {
            return false;
        }

        row = new CsvPrayerTimesRow(
            date, fajr, sunrise, dhuhr, asr, maghrib, isha,
            FajrIqama: GetOptionalTime(cells, map.FajrIqama),
            DhuhrIqama: GetOptionalTime(cells, map.DhuhrIqama),
            AsrIqama: GetOptionalTime(cells, map.AsrIqama),
            MaghribIqama: GetOptionalTime(cells, map.MaghribIqama),
            IshaIqama: GetOptionalTime(cells, map.IshaIqama));
        return true;
    }

    /// <summary>
    /// Returns the positional column layout for a header-less row of the given length,
    /// or <see langword="null"/> when the row has too few columns.
    /// </summary>
    private static ColumnMap? PositionalLayout(int columnCount)
    {
        if (columnCount >= FullLayoutColumns)
        {
            // date, fajr_athan, fajr_iqama, sunrise, dhuhr_athan, dhuhr_iqama,
            // asr_athan, asr_iqama, maghrib_athan, maghrib_iqama, isha_athan, isha_iqama
            return new ColumnMap
            {
                Date = 0,
                FajrAthan = 1,
                FajrIqama = 2,
                Sunrise = 3,
                DhuhrAthan = 4,
                DhuhrIqama = 5,
                AsrAthan = 6,
                AsrIqama = 7,
                MaghribAthan = 8,
                MaghribIqama = 9,
                IshaAthan = 10,
                IshaIqama = 11,
            };
        }

        if (columnCount >= MinAthanOnlyColumns)
        {
            // date, fajr, sunrise, dhuhr, asr, maghrib, isha (athan only)
            return new ColumnMap
            {
                Date = 0,
                FajrAthan = 1,
                Sunrise = 2,
                DhuhrAthan = 3,
                AsrAthan = 4,
                MaghribAthan = 5,
                IshaAthan = 6,
            };
        }

        return null;
    }

    /// <summary>
    /// Attempts to interpret <paramref name="cells"/> as a header row, mapping known
    /// column-name aliases to indices. Returns <see langword="true"/> only when the date
    /// and all six athan columns are recognised, to avoid mistaking a data row for a header.
    /// </summary>
    private static bool TryBuildHeaderMap(List<string> cells, out ColumnMap map)
    {
        map = new ColumnMap();
        var matched = 0;

        for (var i = 0; i < cells.Count; i++)
        {
            var key = NormalizeHeader(cells[i]);
            switch (key)
            {
                case "day" or "date":
                    map.Date = i;
                    break;
                case "fajr_athan" or "fajr_adhan" or "fajr":
                    map.FajrAthan = i;
                    break;
                case "fajr_iqama" or "fajr_iqamah" or "fajr_iqamat":
                    map.FajrIqama = i;
                    break;
                case "sunrise" or "shuruq" or "shurooq":
                    map.Sunrise = i;
                    break;
                case "dhuhr_athan" or "zuhr_athan" or "duhr_athan" or "dhuhr" or "zuhr" or "duhr":
                    map.DhuhrAthan = i;
                    break;
                case "dhuhr_iqama" or "zuhr_iqama" or "duhr_iqama" or "dhuhr_iqamah":
                    map.DhuhrIqama = i;
                    break;
                case "asr_athan" or "asr":
                    map.AsrAthan = i;
                    break;
                case "asr_iqama" or "asr_iqamah":
                    map.AsrIqama = i;
                    break;
                case "maghrib_athan" or "maghrib":
                    map.MaghribAthan = i;
                    break;
                case "maghrib_iqama" or "maghrib_iqamah":
                    map.MaghribIqama = i;
                    break;
                case "isha_athan" or "isha":
                    map.IshaAthan = i;
                    break;
                case "isha_iqama" or "isha_iqamah":
                    map.IshaIqama = i;
                    break;
                default:
                    continue;
            }

            matched++;
        }

        // Require a date column plus the full set of athan columns for a usable header.
        var usable = matched >= 3 &&
            map.Date != Absent &&
            map.FajrAthan != Absent &&
            map.Sunrise != Absent &&
            map.DhuhrAthan != Absent &&
            map.AsrAthan != Absent &&
            map.MaghribAthan != Absent &&
            map.IshaAthan != Absent;

        return usable;
    }

    private static string NormalizeHeader(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed.Replace(' ', '_').Replace('-', '_');
    }

    private static bool TryGetDate(List<string> cells, int index, out DateOnly result)
    {
        result = default;
        if (index < 0 || index >= cells.Count)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            cells[index].Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static bool TryGetTime(List<string> cells, int index, out TimeOnly result)
    {
        result = default;
        if (index < 0 || index >= cells.Count)
        {
            return false;
        }

        return TimeOnly.TryParseExact(
            cells[index].Trim(),
            TimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static TimeOnly? GetOptionalTime(List<string> cells, int index)
        => TryGetTime(cells, index, out var time) ? time : null;

    /// <summary>
    /// Splits a single CSV line into fields, honouring double-quoted fields (which may
    /// contain commas and escaped <c>""</c> quotes) and trimming surrounding whitespace.
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        fields.Add(sb.ToString().Trim());
                        sb.Clear();
                        break;
                    case '\r':
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
        }

        fields.Add(sb.ToString().Trim());
        return fields;
    }

    /// <summary>Mutable column-index lookup; <see cref="Absent"/> (-1) means "not present".</summary>
    private sealed class ColumnMap
    {
        public int Date { get; set; } = Absent;
        public int FajrAthan { get; set; } = Absent;
        public int FajrIqama { get; set; } = Absent;
        public int Sunrise { get; set; } = Absent;
        public int DhuhrAthan { get; set; } = Absent;
        public int DhuhrIqama { get; set; } = Absent;
        public int AsrAthan { get; set; } = Absent;
        public int AsrIqama { get; set; } = Absent;
        public int MaghribAthan { get; set; } = Absent;
        public int MaghribIqama { get; set; } = Absent;
        public int IshaAthan { get; set; } = Absent;
        public int IshaIqama { get; set; } = Absent;
    }
}

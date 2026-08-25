import WidgetKit
import SwiftUI

// TC-04, FR-WG-01, FR-WG-02, FR-WG-04, FR-WG-05, FR-WG-06
// Native WidgetKit extension that reads pre-resolved prayer data from the
// shared App Group container and renders it without hitting the network.

// MARK: - Shared data model

struct WidgetPrayerEntry: Codable, Identifiable {
    let name: String
    let athanTime: String
    let iqamaTime: String
    let athanEpochSeconds: Int64
    let iqamaEpochSeconds: Int64

    var id: String { name }

    // Forward-compatible decoding: an older snapshot without per-prayer epochs still
    // decodes (the epochs default to 0) instead of failing the whole list.
    enum CodingKeys: String, CodingKey {
        case name, athanTime, iqamaTime, athanEpochSeconds, iqamaEpochSeconds
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        athanTime = try c.decodeIfPresent(String.self, forKey: .athanTime) ?? ""
        iqamaTime = try c.decodeIfPresent(String.self, forKey: .iqamaTime) ?? ""
        athanEpochSeconds = try c.decodeIfPresent(Int64.self, forKey: .athanEpochSeconds) ?? 0
        iqamaEpochSeconds = try c.decodeIfPresent(Int64.self, forKey: .iqamaEpochSeconds) ?? 0
    }

}

struct WidgetDay: Codable {
    let dateEpochSeconds: Int64
    let hijriDate: String
    let gregorianDate: String
    let prayers: [WidgetPrayerEntry]

    // Forward-compatible decoding: a day missing newer keys still decodes (FR-WG-05).
    enum CodingKeys: String, CodingKey {
        case dateEpochSeconds, hijriDate, gregorianDate, prayers
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        dateEpochSeconds = try c.decodeIfPresent(Int64.self, forKey: .dateEpochSeconds) ?? 0
        hijriDate = try c.decodeIfPresent(String.self, forKey: .hijriDate) ?? ""
        gregorianDate = try c.decodeIfPresent(String.self, forKey: .gregorianDate) ?? ""
        prayers = try c.decodeIfPresent([WidgetPrayerEntry].self, forKey: .prayers) ?? []
    }
}

struct MasjidWidgetData: Codable {
    let masjidName: String
    let timezone: String
    let generatedAtEpochSeconds: Int64
    let days: [WidgetDay]

    // Forward-compatible defaults so a snapshot missing newer keys still decodes
    // instead of failing the whole widget (FR-WG-05).
    enum CodingKeys: String, CodingKey {
        case masjidName, timezone, generatedAtEpochSeconds, days
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        masjidName = try c.decodeIfPresent(String.self, forKey: .masjidName) ?? ""
        timezone = try c.decodeIfPresent(String.self, forKey: .timezone) ?? ""
        generatedAtEpochSeconds = try c.decodeIfPresent(Int64.self, forKey: .generatedAtEpochSeconds) ?? 0
        days = try c.decodeIfPresent([WidgetDay].self, forKey: .days) ?? []
    }
}

// MARK: - Prayer transition timeline

enum PrayerTransitionKind: String {
    case athan = "Athan"
    case iqama = "Iqama"
}

/// A single athan or iqama point. Built from every prayer across the covered days so
/// countdowns target athan before prayer time and iqama between athan and iqama.
struct PrayerTransition {
    let name: String
    let athan: String
    let iqama: String
    let transitionEpoch: Int64
    let kind: PrayerTransitionKind

    var transitionDate: Date { Date(timeIntervalSince1970: TimeInterval(transitionEpoch)) }
    var displayTime: String { kind == .athan ? athan : iqama }
}

extension MasjidWidgetData {
    /// All transition points across every covered day, sorted chronologically.
    var transitions: [PrayerTransition] {
        days.flatMap { day in
            day.prayers.flatMap { prayer -> [PrayerTransition] in
                var result: [PrayerTransition] = []
                if prayer.athanEpochSeconds > 0 {
                    result.append(PrayerTransition(
                        name: prayer.name,
                        athan: prayer.athanTime,
                        iqama: prayer.iqamaTime,
                        transitionEpoch: prayer.athanEpochSeconds,
                        kind: .athan))
                }
                if prayer.iqamaEpochSeconds > 0 {
                    result.append(PrayerTransition(
                        name: prayer.name,
                        athan: prayer.athanTime,
                        iqama: prayer.iqamaTime,
                        transitionEpoch: prayer.iqamaEpochSeconds,
                        kind: .iqama))
                }
                return result
            }
        }
        .sorted { $0.transitionEpoch < $1.transitionEpoch }
    }

    /// The first transition strictly after `reference`, or nil when none remain in
    /// the snapshot (a reload will have been requested by then).
    func upcomingTransition(after reference: Date) -> PrayerTransition? {
        let refEpoch = Int64(reference.timeIntervalSince1970)
        return transitions.first { $0.transitionEpoch > refEpoch }
    }

    /// The sky background active at `reference`, including Sunrise and Maghrib ±15-minute windows.
    /// Mirrors C# WidgetDaySelector.CurrentPrayerName for Android/iOS parity.
    func currentPrayerName(at reference: Date) -> String? {
        let refEpoch = Int64(reference.timeIntervalSince1970)
        guard let day = currentDay(at: reference) else { return nil }
        func athan(_ name: String) -> Int64 {
            day.prayers.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }?.athanEpochSeconds ?? 0
        }

        let fajr = athan("Fajr")
        let sunrise = athan("Sunrise")
        let asr = athan("Asr")
        let maghrib = athan("Maghrib")
        guard fajr > 0, sunrise > 0, asr > 0, maghrib > 0 else {
            return transitions.last { $0.transitionEpoch <= refEpoch }?.name ?? "Isha"
        }

        let window: Int64 = 15 * 60
        if refEpoch >= sunrise - window && refEpoch < sunrise + window { return "Sunrise" }
        if refEpoch >= maghrib - window && refEpoch < maghrib + window { return "Maghrib" }
        if refEpoch >= fajr && refEpoch < sunrise { return "Fajr" }
        if refEpoch >= sunrise && refEpoch < asr { return "Dhuhr" }
        if refEpoch >= asr && refEpoch < maghrib { return "Asr" }
        return "Isha"
    }

    /// The prayer period active at `reference` using strict athan boundaries.
    func currentPrayerPeriodName(at reference: Date) -> String? {
        let refEpoch = Int64(reference.timeIntervalSince1970)
        let prayers = days
            .flatMap(\.prayers)
            .filter { $0.athanEpochSeconds > 0 }
            .sorted { $0.athanEpochSeconds < $1.athanEpochSeconds }
        return prayers.last { $0.athanEpochSeconds <= refEpoch }?.name ??
            (days.isEmpty ? nil : "Isha")
    }

    /// Start anchor for the progress bar leading up to `next`: the most recent
    /// transition before it, or a 1-hour fallback.
    func periodStart(before next: PrayerTransition) -> Date {
        if let prior = transitions.last(where: { $0.transitionEpoch < next.transitionEpoch }) {
            return prior.transitionDate
        }
        return next.transitionDate.addingTimeInterval(-3600)
    }

    /// Remaining fraction of the interval leading to `next`, from one at the prior
    /// transition to zero at the target.
    func remainingProgress(at reference: Date, toward next: PrayerTransition) -> Double {
        let start = min(periodStart(before: next), next.transitionDate)
        let duration = next.transitionDate.timeIntervalSince(start)
        guard duration > 0 else { return 0 }
        let remaining = next.transitionDate.timeIntervalSince(reference)
        return max(0, min(1, remaining / duration))
    }

    /// The day containing `reference` - the latest day whose start is at or before it.
    /// Falls back to the first day before the window and the last day after it, so the
    /// widget always has a day to render. Mirrors the C# `WidgetDaySelector` for parity.
    func currentDay(at reference: Date) -> WidgetDay? {
        let refEpoch = Int64(reference.timeIntervalSince1970)
        let sorted = days.sorted { $0.dateEpochSeconds < $1.dateEpochSeconds }
        var match: WidgetDay? = sorted.first
        for day in sorted where day.dateEpochSeconds <= refEpoch {
            match = day
        }
        return match
    }

    /// End of the covered window: the last day's start + 24h. Zero when empty.
    var coverageEndEpoch: Int64 {
        guard let last = days.map(\.dateEpochSeconds).max() else { return 0 }
        return last + 86_400
    }

    /// True once `reference` has run past the covered window (data no longer covers
    /// the current day) so the widget should show a staleness message (FR-WG-01).
    func isStale(at reference: Date) -> Bool {
        let end = coverageEndEpoch
        return end > 0 && Int64(reference.timeIntervalSince1970) >= end
    }

    /// Hijri date to display at `reference`, rolling over at each day boundary.
    func displayHijri(at reference: Date) -> String { currentDay(at: reference)?.hijriDate ?? "" }

    /// Gregorian date to display at `reference`, rolling over at each day boundary.
    func displayGregorian(at reference: Date) -> String { currentDay(at: reference)?.gregorianDate ?? "" }

    /// The prayers to display at `reference` (the current day's list).
    func prayers(at reference: Date) -> [WidgetPrayerEntry] { currentDay(at: reference)?.prayers ?? [] }
}

// MARK: - Prayer sky gradients

struct PrayerSkyGradient {
    let top: Color
    let middle: Color
    let bottom: Color

    var linearGradient: LinearGradient {
        LinearGradient(
            colors: [top, middle, bottom],
            startPoint: .top,
            endPoint: .bottom)
    }

    static func forPrayer(_ prayerName: String?) -> PrayerSkyGradient {
        let name = prayerName?.lowercased() ?? ""
        if name.contains("fajr") {
            return sky(top: 0x0E1840, middle: 0x35285C, bottom: 0x8B3D1A)
        }

        if name.contains("sunrise") {
            return sky(top: 0x1B4D8E, middle: 0xC47B1A, bottom: 0xE8AE24)
        }

        if name.contains("dhuhr") {
            return sky(top: 0x0B66B0, middle: 0x1E8FD4, bottom: 0x7DC2E0)
        }

        if name.contains("asr") {
            return sky(top: 0x1B72A8, middle: 0x4A9EBC, bottom: 0xC8A848)
        }

        if name.contains("maghrib") {
            return sky(top: 0x1A2C5A, middle: 0x4A3B68, bottom: 0xA34A1F)
        }

        return sky(top: 0x05101E, middle: 0x0B1C38, bottom: 0x162A48)
    }

    private static func sky(top: UInt32, middle: UInt32, bottom: UInt32) -> PrayerSkyGradient {
        PrayerSkyGradient(
            top: Color(hex: top, opacity: 0.9),
            middle: Color(hex: middle, opacity: 0.9),
            bottom: Color(hex: bottom, opacity: 0.9))
    }
}

extension Color {
    init(hex: UInt32, opacity: Double = 1.0) {
        self.init(
            red: Double((hex >> 16) & 0xFF) / 255.0,
            green: Double((hex >> 8) & 0xFF) / 255.0,
            blue: Double(hex & 0xFF) / 255.0,
            opacity: opacity)
    }
}

extension View {
    @ViewBuilder
    func widgetFallbackBackground(_ gradient: LinearGradient, drawsOwnBackground: Bool) -> some View {
        if drawsOwnBackground {
            self.background(gradient)
        } else {
            self
        }
    }
}

// MARK: - Timeline entry

struct MasjidWidgetEntry: TimelineEntry {
    let date: Date
    let data: MasjidWidgetData?
}

private enum WidgetTimelineSchedule {
    static let minute: TimeInterval = 60
    static let bufferedMinuteCount = 3 * 60
    static let reloadInterval = 60 * minute

    static func minuteDates(after reference: Date) -> [Date] {
        let firstMinuteEpoch = (floor(reference.timeIntervalSince1970 / minute) + 1) * minute
        return (0..<bufferedMinuteCount).map { offset in
            Date(timeIntervalSince1970: firstMinuteEpoch + Double(offset) * minute)
        }
    }

    static func transitionHandoffDates(in data: MasjidWidgetData, after reference: Date) -> [Date] {
        data.transitions.flatMap { transition -> [Date] in
            let transitionDate = transition.transitionDate
            return [transitionDate, transitionDate.addingTimeInterval(1)]
        }
        .filter { $0 > reference }
    }

    static func coverageEndDate(in data: MasjidWidgetData, after reference: Date) -> Date? {
        guard data.coverageEndEpoch > 0 else { return nil }
        let coverageEnd = Date(timeIntervalSince1970: TimeInterval(data.coverageEndEpoch))
        return coverageEnd > reference ? coverageEnd : nil
    }

    static func entries(
        for dates: [Date],
        data: MasjidWidgetData) -> [MasjidWidgetEntry] {
        Set(dates).sorted().map { MasjidWidgetEntry(date: $0, data: data) }
    }
}

// MARK: - Data loader

private func loadWidgetData() -> MasjidWidgetData? {
    let decoder = JSONDecoder()
    decoder.keyDecodingStrategy = .convertFromSnakeCase

    if let container = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: "group.com.goodsoftware.masjidapp") {
        let fileUrl = container.appendingPathComponent("widget_data.json")
        if let jsonData = try? Data(contentsOf: fileUrl),
           let data = try? decoder.decode(MasjidWidgetData.self, from: jsonData) {
            return data
        }
    }

    let defaults = UserDefaults(suiteName: "group.com.goodsoftware.masjidapp")
    guard let json = defaults?.string(forKey: "widget_data"),
          let jsonData = json.data(using: .utf8) else { return nil }
    return try? decoder.decode(MasjidWidgetData.self, from: jsonData)
}

// MARK: - Timeline provider

struct MasjidWidgetProvider: TimelineProvider {
    func placeholder(in context: Context) -> MasjidWidgetEntry {
        MasjidWidgetEntry(date: Date(), data: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (MasjidWidgetEntry) -> Void) {
        completion(MasjidWidgetEntry(date: Date(), data: loadWidgetData()))
    }

    /// Builds a timeline with one entry at every upcoming prayer transition, prayer
    /// period start, day boundary, and when the covered window ends, so the live
    /// countdown, progress bar, sky gradient, Hijri date, and staleness state all
    /// advance without a background wake, then requests a reload (FR-WG-04, FR-WG-06).
    func getTimeline(in context: Context, completion: @escaping (Timeline<MasjidWidgetEntry>) -> Void) {
        let data = loadWidgetData()
        let now = Date()

        // Always render the current state first.
        var entries: [MasjidWidgetEntry] = [MasjidWidgetEntry(date: now, data: data)]

        if let data = data {
            // Exact transition entries switch the target. A one-second handoff entry
            // prevents a coalesced boundary entry from leaving the completed target active.
            var breakpoints = WidgetTimelineSchedule.transitionHandoffDates(
                in: data,
                after: now)

            for day in data.days {
                for prayer in day.prayers where prayer.athanEpochSeconds > 0 {
                    let athan = Date(timeIntervalSince1970: TimeInterval(prayer.athanEpochSeconds))
                    let normalizedName = prayer.name.lowercased()
                    if normalizedName == "sunrise" || normalizedName == "maghrib" {
                        let window: TimeInterval = 15 * 60
                        let windowStart = athan.addingTimeInterval(-window)
                        let windowEnd = athan.addingTimeInterval(window)
                        if windowStart > now { breakpoints.append(windowStart) }
                        if windowEnd > now { breakpoints.append(windowEnd) }
                    }
                }
            }

            // Each day's start flips the displayed Hijri/Gregorian date (FR-WG-06).
            for day in data.days {
                let start = Date(timeIntervalSince1970: TimeInterval(day.dateEpochSeconds))
                if start > now { breakpoints.append(start) }
            }

            // Coverage end → switch into the stale state (FR-WG-01).
            if let coverageEnd = WidgetTimelineSchedule.coverageEndDate(in: data, after: now) {
                breakpoints.append(coverageEnd)
            }

            for date in Set(breakpoints).sorted() {
                entries.append(MasjidWidgetEntry(date: date, data: data))
            }
        }

        // Reload policy: retry soon if no snapshot yet; otherwise re-evaluate when the
        // covered window ends (to surface staleness / pick up a fresher snapshot), with
        // a 6-hour safety net so a still-fresh widget re-reads periodically.
        let reloadDate: Date
        if let data = data {
            let end = data.coverageEndEpoch
            let safetyNet = now.addingTimeInterval(6 * 3600)
            if end > 0 {
                let endDate = Date(timeIntervalSince1970: TimeInterval(end))
                reloadDate = endDate > now ? min(endDate, safetyNet) : now.addingTimeInterval(30 * 60)
            } else {
                reloadDate = safetyNet
            }
        } else {
            reloadDate = Calendar.current.date(byAdding: .minute, value: 1, to: now)!
        }
        let timeline = Timeline(entries: entries, policy: .after(reloadDate))
        completion(timeline)
    }
}

struct MasjidCountdownTimelineProvider: TimelineProvider {
    func placeholder(in context: Context) -> MasjidWidgetEntry {
        MasjidWidgetEntry(date: Date(), data: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (MasjidWidgetEntry) -> Void) {
        completion(MasjidWidgetEntry(date: Date(), data: loadWidgetData()))
    }

    /// Keeps iOS 15 and Lock Screen gauges current with buffered minute entries. The
    /// iOS 16+ home-screen ring advances natively, while exact and one-second handoff
    /// entries switch all countdown variants to the next athan or iqama (FR-WG-04).
    func getTimeline(in context: Context, completion: @escaping (Timeline<MasjidWidgetEntry>) -> Void) {
        let data = loadWidgetData()
        let now = Date()
        guard let data = data else {
            completion(Timeline(
                entries: [MasjidWidgetEntry(date: now, data: nil)],
                policy: .after(now.addingTimeInterval(60))))
            return
        }

        var dates = [now]
        dates.append(contentsOf: WidgetTimelineSchedule.minuteDates(after: now))
        dates.append(contentsOf: WidgetTimelineSchedule.transitionHandoffDates(
            in: data,
            after: now))
        if let coverageEnd = WidgetTimelineSchedule.coverageEndDate(in: data, after: now) {
            dates.append(coverageEnd)
        }

        completion(Timeline(
            entries: WidgetTimelineSchedule.entries(for: dates, data: data),
            policy: .after(now.addingTimeInterval(WidgetTimelineSchedule.reloadInterval))))
    }
}

// MARK: - Widget formatting

/// Drops a trailing "AM"/"PM" (with any preceding space) so compact widgets can
/// render larger, more legible times without truncation.
private func stripMeridiem(_ time: String) -> String {
    guard let range = time.range(of: #"\s*[APap][Mm]$"#, options: .regularExpression) else {
        return time
    }
    return String(time[..<range.lowerBound])
}

private func isSunrise(_ transition: PrayerTransition) -> Bool {
    transition.name.caseInsensitiveCompare("Sunrise") == .orderedSame
}

private func compactCountdownLabel(for transition: PrayerTransition) -> String {
    isSunrise(transition) ? "until" : "till \(transition.kind.rawValue)"
}

private func compactHijriDate(_ date: String) -> String {
    let parts = date.split(separator: " ")
    guard parts.count >= 3 else { return date.uppercased() }

    let day = String(parts[0])
    let month = parts.dropFirst().dropLast().joined(separator: " ")
    let shortenedMonth = switch month {
    case "Muharram": "MUHR"
    case "Safar": "SAFR"
    case "Rabi' al-Awwal": "RAB1"
    case "Rabi' al-Thani": "RAB2"
    case "Jumada al-Awwal": "JUM1"
    case "Jumada al-Thani": "JUM2"
    case "Rajab": "RAJB"
    case "Sha'ban": "SHBN"
    case "Ramadan": "RAMD"
    case "Shawwal": "SHWL"
    case "Dhul-Qa'dah": "DHQD"
    case "Dhul-Hijjah": "DHHJ"
    default: String(month.prefix(4)).uppercased()
    }
    return "\(shortenedMonth) \(day)"
}

private func compactGregorianDate(_ date: String) -> String {
    let dateParts = date.split(separator: ",", maxSplits: 1)
    let components = (dateParts.last ?? Substring(date)).split(separator: " ")
    guard components.count >= 2 else { return date.uppercased() }

    let day = String(components[0])
    let month = String(components[1].prefix(3)).uppercased()
    return "\(month) \(day)"
}

private func lockScreenLabel(for transition: PrayerTransition) -> String {
    if transition.kind == .iqama {
        return "IQM"
    }

    return switch transition.name.lowercased() {
    case "fajr": "FJR"
    case "sunrise": "SUN"
    case "dhuhr": "DHR"
    case "asr": "ASR"
    case "maghrib": "MGB"
    case "isha": "ISH"
    default: String(transition.name.prefix(3)).uppercased()
    }
}

private func staticCountdownText(from reference: Date, to target: Date) -> String {
    let totalSeconds = max(0, Int(ceil(target.timeIntervalSince(reference))))
    let hours = totalSeconds / 3600
    let minutes = (totalSeconds % 3600) / 60
    let seconds = totalSeconds % 60
    return hours > 0
        ? String(format: "%d:%02d:%02d", hours, minutes, seconds)
        : String(format: "%d:%02d", minutes, seconds)
}

private struct CountdownTransitionDetailsView: View {
    let transition: PrayerTransition
    let scale: CGFloat

    var body: some View {
        VStack(spacing: scale) {
            Text(compactCountdownLabel(for: transition))
                .font(.system(size: 12 * scale, weight: .medium))
                .foregroundColor(.white.opacity(0.76))
            Text(transition.name)
                .font(.system(size: 20 * scale, weight: .bold))
                .foregroundColor(.white)
                .lineLimit(1)
            Text(stripMeridiem(transition.displayTime))
                .font(.system(size: 15 * scale, weight: .bold))
                .foregroundColor(.white)
                .monospacedDigit()
                .lineLimit(1)
        }
    }
}

private struct CountdownCircleView: View {
    let reference: Date
    let data: MasjidWidgetData
    let transition: PrayerTransition

    var body: some View {
        let target = transition.transitionDate
        let progress = CGFloat(data.remainingProgress(at: reference, toward: transition))
        let countdownStart = min(reference, target)
        ZStack {
            Circle()
                .stroke(Color.white.opacity(0.18), lineWidth: 8)
            Circle()
                .trim(from: 0, to: max(0.025, progress))
                .stroke(
                    Color(hex: 0x45D89A),
                    style: StrokeStyle(lineWidth: 8, lineCap: .round))
                .rotationEffect(.degrees(-90))

            VStack(spacing: 1) {
                if #available(iOSApplicationExtension 16.0, *) {
                    Text(timerInterval: countdownStart...target, countsDown: true)
                        .font(.system(size: 24, weight: .bold, design: .rounded))
                        .foregroundColor(.white)
                        .monospacedDigit()
                        .lineLimit(1)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: .infinity, alignment: .center)
                } else {
                    Text(staticCountdownText(from: reference, to: target))
                        .font(.system(size: 24, weight: .bold, design: .rounded))
                        .foregroundColor(.white)
                        .monospacedDigit()
                        .lineLimit(1)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: .infinity, alignment: .center)
                }
                CountdownTransitionDetailsView(transition: transition, scale: 1)
            }
            .padding(9)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Compact widget view (widget-1: two rows - athan above, iqama below)
// FR-WG-01: shows next prayer name, athan/iqama times, and countdown.
// The horizontal layout shows: header (Hijri date + masjid name), a progress bar
// to the next transition, a live "H:MM:SS till <prayer> <Athan|Iqama>" countdown, and all
// prayers in two rows - athan on top, iqama beneath.

struct CompactWidgetView: View {
    let entry: MasjidWidgetEntry
    let drawsOwnBackground: Bool

    /// Reference instant for this timeline entry. The view derives the next prayer,
    /// progress window, and date from this so each entry shows the correct period.
    private var reference: Date { entry.date }

    private func countdownLabel(for transition: PrayerTransition) -> String {
        isSunrise(transition)
            ? "until Sunrise"
            : "till \(transition.name) \(transition.kind.rawValue)"
    }

    var body: some View {
        if let data = entry.data {
            let next = data.upcomingTransition(after: reference)
            let stale = data.isStale(at: reference)
            let currentGradient = PrayerSkyGradient.forPrayer(data.currentPrayerName(at: reference)).linearGradient
            VStack(alignment: .leading, spacing: 0) {
                // Header: Hijri date (accent) on the left, masjid name on the right.
                HStack {
                    Text(data.displayHijri(at: reference))
                        .font(.subheadline)
                        .foregroundColor(Color(hex: 0xFFCC00))
                        .lineLimit(1)
                    Spacer()
                    Text(data.masjidName)
                        .font(.subheadline)
                        .foregroundColor(.white.opacity(0.82))
                        .lineLimit(1)
                }

                Spacer(minLength: 8)

                // Staleness message (FR-WG-01): the snapshot no longer covers today.
                if stale {
                    Text("Times may be outdated - open the app")
                        .font(.caption2)
                        .foregroundColor(.orange)
                        .lineLimit(2)
                        .minimumScaleFactor(0.8)
                }

                // Progress bar to the next transition + live countdown (FR-WG-01).
                if let next = next {
                    let target = next.transitionDate
                    // Clamp the lower bounds: WidgetKit traps (and permanently drops the
                    // widget to its placeholder) if a ClosedRange is built with
                    // lowerBound > upperBound. Guarding here means an edge or malformed
                    // snapshot degrades gracefully instead of crashing (FR-WG-05, NFR-RL-01).
                    let progressStart = min(data.periodStart(before: next), target)
                    let countdownStart = min(reference, target)
                    if #available(iOSApplicationExtension 16.0, *) {
                        ProgressView(timerInterval: progressStart...target, countsDown: true)
                            .progressViewStyle(.linear)
                            .tint(Color(hex: 0xFFCC00))
                            .labelsHidden()
                            .scaleEffect(x: 1, y: 1.35, anchor: .center)

                        Spacer(minLength: 8)

                        HStack(spacing: 4) {
                            Text(timerInterval: countdownStart...target, countsDown: true)
                                .font(.title2.bold())
                                .foregroundColor(.white)
                                .monospacedDigit()
                                .lineLimit(1)
                            Text(countdownLabel(for: next))
                                .font(.system(size: 16, weight: .semibold))
                                .foregroundColor(.white.opacity(0.88))
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                                .allowsTightening(true)
                        }
                    } else {
                        // Pre-iOS 16 fallback: static next-prayer line.
                        HStack {
                            Text(next.name)
                                .font(.headline)
                                .foregroundColor(.white)
                            Spacer()
                            Text(next.athan)
                                .font(.subheadline)
                                .foregroundColor(.white.opacity(0.88))
                        }
                    }
                }

                Spacer(minLength: 8)

                Divider().overlay(Color.white.opacity(0.35))

                Spacer(minLength: 6)

                // All prayers - two rows: iqama on top (highlighted), athan beneath (FR-WG-01).
                // AM/PM is stripped here (not from the shared snapshot) so this compact,
                // per-prayer column layout can render larger, more legible text.
                HStack(spacing: 2) {
                    ForEach(data.prayers(at: reference)) { prayer in
                        VStack(spacing: 1) {
                            Text(prayer.name)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundColor(.white.opacity(0.78))
                                .lineLimit(1)
                                .minimumScaleFactor(0.7)
                            // Iqama is the primary time; athan is secondary.
                            Text(stripMeridiem(prayer.iqamaTime.isEmpty ? prayer.athanTime : prayer.iqamaTime))
                                .font(.system(size: 18, weight: .bold))
                                .foregroundColor(.white)
                                .monospacedDigit()
                                .lineLimit(1)
                                .minimumScaleFactor(0.7)
                            Text(prayer.iqamaTime.isEmpty ? "" : stripMeridiem(prayer.athanTime))
                                .font(.system(size: 14))
                                .foregroundColor(.white.opacity(0.78))
                                .monospacedDigit()
                                .lineLimit(1)
                                .minimumScaleFactor(0.7)
                        }
                        .frame(maxWidth: .infinity)
                    }
                }

                Spacer(minLength: 0)
            }
            .padding(12)
            .widgetFallbackBackground(currentGradient, drawsOwnBackground: drawsOwnBackground)
        } else {
            Text("Open the app to configure your masjid.")
                .font(.caption)
                .foregroundColor(.white)
                .multilineTextAlignment(.center)
                .padding(12)
                .widgetFallbackBackground(PrayerSkyGradient.forPrayer(nil).linearGradient, drawsOwnBackground: drawsOwnBackground)
        }
    }
}

// MARK: - Small iqama-list widget (widget-3: today's iqama times vertically)

struct IqamaListWidgetView: View {
    let entry: MasjidWidgetEntry
    let drawsOwnBackground: Bool

    var body: some View {
        if let data = entry.data {
            let currentGradient = PrayerSkyGradient.forPrayer(
                data.currentPrayerName(at: entry.date)).linearGradient
            let currentPrayerName = data.currentPrayerPeriodName(at: entry.date)
            VStack(alignment: .leading, spacing: 0) {
                Text(data.masjidName)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(.white)
                    .lineLimit(1)

                if let day = data.currentDay(at: entry.date) {
                    Text(
                        "\(compactHijriDate(day.hijriDate)) • \(compactGregorianDate(day.gregorianDate))")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(Color(hex: 0xFFCC00))
                        .lineLimit(1)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: .infinity, alignment: .center)
                }

                if data.isStale(at: entry.date) {
                    Spacer()
                    Text("Times may be outdated")
                        .font(.caption2)
                        .foregroundColor(.orange)
                        .lineLimit(2)
                    Text("Open the app to update")
                        .font(.caption2)
                        .foregroundColor(.white.opacity(0.82))
                        .lineLimit(2)
                    Spacer()
                } else {
                    ForEach(data.prayers(at: entry.date)) { prayer in
                        let isCurrent = prayer.name == currentPrayerName
                        HStack(spacing: 5) {
                            Image("prayer_\(prayer.name.lowercased())")
                                .resizable()
                                .renderingMode(.template)
                                .foregroundColor(
                                    isCurrent ? Color(hex: 0xFFCC00) : .white.opacity(0.78))
                                .frame(width: 14, height: 14)
                            Text(prayer.name)
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(
                                    isCurrent ? Color(hex: 0xFFCC00) : .white.opacity(0.82))
                                .lineLimit(1)
                            Spacer(minLength: 2)
                            Text(stripMeridiem(
                                prayer.iqamaTime.isEmpty ? prayer.athanTime : prayer.iqamaTime))
                                .font(.system(size: 15, weight: .bold))
                                .foregroundColor(isCurrent ? Color(hex: 0xFFCC00) : .white)
                                .monospacedDigit()
                                .lineLimit(1)
                        }
                        .frame(maxHeight: .infinity)
                    }
                }
            }
            .padding(9)
            .widgetFallbackBackground(currentGradient, drawsOwnBackground: drawsOwnBackground)
        } else {
            Text("Open the app to configure your masjid.")
                .font(.caption)
                .foregroundColor(.white)
                .multilineTextAlignment(.center)
                .padding(10)
                .widgetFallbackBackground(
                    PrayerSkyGradient.forPrayer(nil).linearGradient,
                    drawsOwnBackground: drawsOwnBackground)
        }
    }
}

// MARK: - Small countdown widget (widget-4: remaining time and iqama)

struct CountdownWidgetView: View {
    let entry: MasjidWidgetEntry
    let drawsOwnBackground: Bool

    var body: some View {
        if let data = entry.data {
            let next = data.upcomingTransition(after: entry.date)
            let currentGradient = PrayerSkyGradient.forPrayer(
                data.currentPrayerName(at: entry.date)).linearGradient
            let day = data.currentDay(at: entry.date)
            VStack(spacing: 2) {
                Text(data.masjidName)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(.white)
                    .lineLimit(1)
                    .frame(maxWidth: .infinity, alignment: .leading)

                if data.isStale(at: entry.date) {
                    Spacer()
                    Text("Times may be outdated")
                        .font(.caption2)
                        .foregroundColor(.orange)
                    Text("Open the app to update")
                        .font(.caption2)
                        .foregroundColor(.white.opacity(0.82))
                    Spacer()
                } else if let next = next {
                    CountdownCircleView(
                        reference: entry.date,
                        data: data,
                        transition: next)
                } else {
                    Spacer()
                    Text("Open the app to update prayer times")
                        .font(.caption2)
                        .foregroundColor(.white)
                        .multilineTextAlignment(.center)
                    Spacer()
                }

                if let day = day {
                    HStack {
                        Text(compactHijriDate(day.hijriDate))
                            .foregroundColor(.white)
                        Spacer(minLength: 4)
                        Text(compactGregorianDate(day.gregorianDate))
                            .foregroundColor(.white.opacity(0.82))
                    }
                    .font(.system(size: 12, weight: .semibold))
                    .lineLimit(1)
                }
            }
            .padding(9)
            .widgetFallbackBackground(currentGradient, drawsOwnBackground: drawsOwnBackground)
        } else {
            Text("Open the app to configure your masjid.")
                .font(.caption)
                .foregroundColor(.white)
                .multilineTextAlignment(.center)
                .padding(10)
                .widgetFallbackBackground(
                    PrayerSkyGradient.forPrayer(nil).linearGradient,
                    drawsOwnBackground: drawsOwnBackground)
        }
    }
}

// MARK: - Lock-screen circular gauge

@available(iOSApplicationExtension 16.0, *)
struct LockScreenGaugeWidgetView: View {
    let entry: MasjidWidgetEntry

    var body: some View {
        if let data = entry.data,
           !data.isStale(at: entry.date),
           let next = data.upcomingTransition(after: entry.date) {
            ZStack {
                Gauge(value: data.remainingProgress(at: entry.date, toward: next)) {
                    EmptyView()
                } currentValueLabel: {
                    EmptyView()
                }
                .gaugeStyle(.accessoryCircular)

                VStack(spacing: 0) {
                    Text(stripMeridiem(next.displayTime))
                        .font(.system(size: 18, design: .rounded))
                        .monospacedDigit()
                        .lineLimit(1)
                        .minimumScaleFactor(0.75)

                    Spacer(minLength: 0)

                    Text(lockScreenLabel(for: next))
                        .font(.system(size: 12))
                        .lineLimit(1)
                }
                .padding(.top, 18)
                .padding(.bottom, 1)
            }
            .widgetAccentable()
        } else {
            Gauge(value: 0) {
                EmptyView()
            } currentValueLabel: {
                Text("--")
                    .font(.system(size: 18, weight: .bold, design: .rounded))
            }
            .gaugeStyle(.accessoryCircular)
        }
    }
}

// MARK: - Full widget view (widget-2: all prayers, two columns - athan | iqama)
// FR-WG-02: medium/large size shows all of today's prayers with both time columns.

struct FullWidgetView: View {
    let entry: MasjidWidgetEntry

    var body: some View {
        if let data = entry.data {
            // Highlight the prayer that is current relative to this entry's instant, and
            // roll the displayed date over at each day boundary (FR-WG-06).
            let currentPrayerName = data.currentPrayerPeriodName(at: entry.date)
            let stale = data.isStale(at: entry.date)
            let currentGradient = PrayerSkyGradient.forPrayer(data.currentPrayerName(at: entry.date)).linearGradient
            VStack(alignment: .leading, spacing: 6) {
                // Header: masjid name, then full Hijri and Gregorian dates.
                Text(data.masjidName)
                    .font(.system(size: 22, weight: .bold))
                    .foregroundColor(.white)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                Text(data.displayHijri(at: entry.date))
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(Color(hex: 0xFFCC00))
                    .lineLimit(1)
                Text(data.displayGregorian(at: entry.date))
                    .font(.system(size: 14))
                    .foregroundColor(.white.opacity(0.82))
                    .lineLimit(1)

                Divider().overlay(Color.white.opacity(0.35))

                // Staleness message (FR-WG-01): the snapshot no longer covers today.
                if stale {
                    Text("Prayer times may be outdated - open the app to update.")
                        .font(.caption)
                        .foregroundColor(.orange)
                        .lineLimit(2)
                }

                // Column headers
                HStack {
                    Text("Prayer")
                        .font(.system(size: 18, weight: .semibold))
                        .foregroundColor(.white.opacity(0.72))
                        .frame(minWidth: 60, alignment: .leading)
                    Spacer(minLength: 8)
                    HStack(spacing: 3) {
                        Image("prayer_athan").resizable().renderingMode(.template).frame(width: 18, height: 18)
                        Text("Athan").font(.system(size: 18, weight: .semibold))
                    }
                    .foregroundColor(.white.opacity(0.72))
                    .frame(width: 92, alignment: .trailing)
                    HStack(spacing: 3) {
                        Image("prayer_iqama").resizable().renderingMode(.template).frame(width: 18, height: 18)
                        Text("Iqama").font(.system(size: 18, weight: .semibold))
                    }
                    .foregroundColor(.white.opacity(0.72))
                    .frame(width: 92, alignment: .trailing)
                }

                // One row per prayer; iqama is highlighted, athan is secondary (FR-WG-02).
                ForEach(data.prayers(at: entry.date)) { prayer in
                    let isCurrent = prayer.name == currentPrayerName
                    HStack {
                        Image("prayer_\(prayer.name.lowercased())")
                            .resizable()
                            .renderingMode(.template)
                            .foregroundColor(isCurrent ? Color(hex: 0xFFCC00) : .white)
                            .frame(width: 24, height: 24)
                        Text(prayer.name)
                            .font(.system(size: 22, weight: isCurrent ? .bold : .regular))
                            .foregroundColor(isCurrent ? Color(hex: 0xFFCC00) : .white)
                            .frame(minWidth: 60, alignment: .leading)
                        Spacer(minLength: 8)
                        Text(prayer.athanTime)
                            .font(.system(size: 20, weight: isCurrent ? .bold : .regular))
                            .foregroundColor(isCurrent ? Color(hex: 0xFFCC00) : .white.opacity(0.78))
                            .minimumScaleFactor(0.85)
                            .lineLimit(1)
                            .frame(width: 92, alignment: .trailing)
                        Text(prayer.iqamaTime.isEmpty ? "-" : prayer.iqamaTime)
                            .font(.system(size: 20, weight: isCurrent ? .bold : .regular))
                            .foregroundColor(isCurrent ? Color(hex: 0xFFCC00) : .white)
                            .minimumScaleFactor(0.85)
                            .lineLimit(1)
                            .frame(width: 92, alignment: .trailing)
                    }
                    .frame(maxHeight: .infinity)
                }
            }
            .padding(12)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(currentGradient)
        } else {
            Text("Open the app to set up your masjid.")
                .font(.caption)
                .foregroundColor(.white)
                .multilineTextAlignment(.center)
                .padding(12)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .center)
                .background(PrayerSkyGradient.forPrayer(nil).linearGradient)
        }
    }
}

// MARK: - Widget definitions

/// Small / medium compact next-prayer widget.
struct MasjidWidget: Widget {
    let kind = "MasjidWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: MasjidWidgetProvider()) { entry in
            if #available(iOSApplicationExtension 17.0, *) {
                CompactWidgetView(entry: entry, drawsOwnBackground: false)
                    .containerBackground(PrayerSkyGradient.forPrayer(entry.data?.currentPrayerName(at: entry.date)).linearGradient, for: .widget)
            } else {
                CompactWidgetView(entry: entry, drawsOwnBackground: true)
            }
        }
        .configurationDisplayName("Prayer Times")
        .description("Shows the next prayer with athan and iqama times.")
        .supportedFamilies([.systemMedium])
        .contentMarginsDisabled()
    }
}

/// Medium / large "all prayers" widget (FR-WG-02).
struct MasjidFullWidget: Widget {
    let kind = "MasjidFullWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: MasjidWidgetProvider()) { entry in
            if #available(iOSApplicationExtension 17.0, *) {
                FullWidgetView(entry: entry)
                    .containerBackground(PrayerSkyGradient.forPrayer(entry.data?.currentPrayerName(at: entry.date)).linearGradient, for: .widget)
            } else {
                FullWidgetView(entry: entry)
            }
        }
        .configurationDisplayName("All Prayer Times")
        .description("Shows all of today's prayer times with athan and iqama columns.")
        .supportedFamilies([.systemLarge])
        .contentMarginsDisabled()
    }
}

/// Small vertical list of today's iqama times (FR-WG-01).
struct MasjidIqamaWidget: Widget {
    let kind = "MasjidIqamaWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: MasjidWidgetProvider()) { entry in
            if #available(iOSApplicationExtension 17.0, *) {
                IqamaListWidgetView(entry: entry, drawsOwnBackground: false)
                    .containerBackground(
                        PrayerSkyGradient.forPrayer(
                            entry.data?.currentPrayerName(at: entry.date)).linearGradient,
                        for: .widget)
            } else {
                IqamaListWidgetView(entry: entry, drawsOwnBackground: true)
            }
        }
        .configurationDisplayName("Today's Iqama Times")
        .description("Shows today's iqama times in a compact vertical list.")
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

/// Small live next-athan-or-iqama countdown widget (FR-WG-01).
struct MasjidCountdownWidget: Widget {
    let kind = "MasjidCountdownWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: MasjidCountdownTimelineProvider()) { entry in
            if #available(iOSApplicationExtension 17.0, *) {
                CountdownWidgetView(entry: entry, drawsOwnBackground: false)
                    .containerBackground(
                        PrayerSkyGradient.forPrayer(
                            entry.data?.currentPrayerName(at: entry.date)).linearGradient,
                        for: .widget)
            } else {
                CountdownWidgetView(entry: entry, drawsOwnBackground: true)
            }
        }
        .configurationDisplayName("Next Prayer Countdown")
        .description("Shows the time remaining until the next athan or iqama.")
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

/// Lock-screen circular gauge for the next athan or iqama (FR-WG-01, FR-WG-04).
@available(iOSApplicationExtension 16.0, *)
struct MasjidLockScreenGaugeWidget: Widget {
    let kind = "MasjidLockScreenGaugeWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: MasjidCountdownTimelineProvider()) { entry in
            LockScreenGaugeWidgetView(entry: entry)
        }
        .configurationDisplayName("Next Prayer Gauge")
        .description("Shows the next athan or iqama time on the Lock Screen.")
        .supportedFamilies([.accessoryCircular])
    }
}

// MARK: - Widget bundle entry point

@main
struct MasjidWidgetBundle: WidgetBundle {
    var body: some Widget {
        MasjidWidget()
        MasjidFullWidget()
        MasjidIqamaWidget()
        MasjidCountdownWidget()
        if #available(iOSApplicationExtension 16.1, *) {
            MasjidLockScreenGaugeWidget()
        }
    }
}

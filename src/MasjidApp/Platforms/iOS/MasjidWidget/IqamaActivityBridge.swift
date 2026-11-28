import ActivityKit
import Foundation

public typealias TokenCallback = @convention(c) (UnsafePointer<CChar>, Int64) -> Void

// DllImport resolves this symbol in the app binary, so it must be public.
@_cdecl("MasjidAppSyncIqamaActivities")
public func syncIqamaActivities(_ json: UnsafePointer<CChar>, _ revision: Int64, _ onToken: TokenCallback?) {
    guard #available(iOS 26.0, *),
          let data = String(cString: json).data(using: .utf8),
          let slots = try? JSONDecoder().decode([IqamaSlot].self, from: data)
    else { return }

    Task { @MainActor in
        await IqamaActivities.shared.sync(slots, revision: revision, onToken: onToken)
    }
}

@available(iOS 26.0, *)
private struct IqamaSlot: Decodable {
    let id: String
    let prayer: String
    let countdownStart: Double
    let iqama: Double
    let alertTitle: String
    let alertBody: String

    var attributes: IqamaActivityAttributes {
        IqamaActivityAttributes(id: id, prayer: prayer, countdownStart: countdownStart, iqama: iqama)
    }
}

@available(iOS 26.0, *)
@MainActor
private final class IqamaActivities {
    static let shared = IqamaActivities()
    private typealias IqamaActivity = Activity<IqamaActivityAttributes>

    private let issuedKey = "issuedIqamaLiveActivities"
    private var issued = UserDefaults.standard.dictionary(forKey: "issuedIqamaLiveActivities") as? [String: Double] ?? [:]
    private var scheduled: [String: IqamaActivity] = [:]
    private var lifecycleTasks: [String: Task<Void, Never>] = [:]
    private var tokenTasks: [String: Task<Void, Never>] = [:]
    private var revision: Int64 = 0

    func sync(_ slots: [IqamaSlot], revision currentRevision: Int64, onToken: TokenCallback?) async {
        guard currentRevision > revision else { return }
        revision = currentRevision
        let now = Date.now
        let enabled = ActivityAuthorizationInfo().areActivitiesEnabled
        let upcoming = enabled ? slots.filter { $0.attributes.endDate > now } : []
        let wanted = Set(upcoming.map(\.id))
        issued = issued.filter { $0.value > now.timeIntervalSince1970 && wanted.contains($0.key) }

        for activity in IqamaActivity.activities {
            scheduled[activity.id] = activity
        }
        for activity in Array(scheduled.values) {
            if activity.activityState == .ended || activity.activityState == .dismissed {
                removeObservers(activity.id)
            } else if !wanted.contains(activity.attributes.id) {
                removeObservers(activity.id)
                await activity.end(nil, dismissalPolicy: .immediate)
                guard currentRevision == revision else { return }
            }
        }

        var existing = Set(scheduled.values.map { $0.attributes.id })
#if targetEnvironment(simulator)
        // The ad-hoc simulator build has no APNs entitlement.
        let pushType: PushType? = nil
#else
        let pushType: PushType? = .token
#endif
        for slot in upcoming {
            let attributes = slot.attributes
            guard !existing.contains(slot.id), issued[slot.id] == nil, attributes.iqamaDate > .now else { continue }
            let content = countdownContent(attributes)
            do {
                let activity: IqamaActivity
                if attributes.countdownDate > .now {
                    let alert = AlertConfiguration(
                        title: "\(slot.alertTitle)", body: "\(slot.alertBody)", sound: .default)
                    activity = try IqamaActivity.request(
                        attributes: attributes, content: content, pushType: pushType,
                        style: .standard, alertConfiguration: alert, start: attributes.countdownDate)
                } else {
                    activity = try IqamaActivity.request(attributes: attributes, content: content, pushType: pushType)
                }
                scheduled[activity.id] = activity
                existing.insert(slot.id)
                issued[slot.id] = attributes.endDate.timeIntervalSince1970
            } catch {
                // The system's capacity is shared by pending and active activities.
                // Retry the remaining slots on the next foreground reconciliation.
                NSLog("Could not schedule iqama Live Activity: %@", error.localizedDescription)
                break
            }
        }
        UserDefaults.standard.set(issued, forKey: issuedKey)

        for activity in scheduled.values {
            observe(activity, onToken: onToken)
        }
    }

    private func countdownContent(_ attributes: IqamaActivityAttributes) -> ActivityContent<IqamaActivityAttributes.ContentState> {
        // Prefer the earliest iqama when two reminder windows overlap.
        ActivityContent(
            state: .init(started: false), staleDate: attributes.iqamaDate,
            relevanceScore: 1 / max(1, attributes.iqamaDate.timeIntervalSinceReferenceDate))
    }

    private func observe(_ activity: IqamaActivity, onToken: TokenCallback?) {
        let attributes = activity.attributes
        if let onToken {
            if let token = activity.pushToken {
                report(token, iqama: attributes.iqama, onToken: onToken)
            }
            if tokenTasks[activity.id] == nil {
                tokenTasks[activity.id] = Task { [weak self] in
                    for await token in activity.pushTokenUpdates {
                        guard !Task.isCancelled else { return }
                        self?.report(token, iqama: attributes.iqama, onToken: onToken)
                    }
                }
            }
        }
        guard lifecycleTasks[activity.id] == nil else { return }
        lifecycleTasks[activity.id] = Task { [weak self] in
            await Self.waitUntil(attributes.iqamaDate)
            guard !Task.isCancelled else { return }
            if Date.now < attributes.endDate {
                await activity.update(.init(state: .init(started: true), staleDate: attributes.endDate))
                await Self.waitUntil(attributes.endDate)
            }
            guard !Task.isCancelled else { return }
            await activity.end(nil, dismissalPolicy: .immediate)
            self?.removeObservers(activity.id)
        }
    }

    private func report(_ token: Data, iqama: Double, onToken: TokenCallback) {
        let hex = token.map { String(format: "%02x", $0) }.joined()
        hex.withCString { onToken($0, Int64(iqama)) }
    }

    private func removeObservers(_ id: String) {
        scheduled.removeValue(forKey: id)
        tokenTasks.removeValue(forKey: id)?.cancel()
        lifecycleTasks.removeValue(forKey: id)?.cancel()
    }

    private static func waitUntil(_ date: Date) async {
        let seconds = date.timeIntervalSinceNow
        if seconds > 0 {
            try? await Task.sleep(for: .seconds(seconds))
        }
    }
}

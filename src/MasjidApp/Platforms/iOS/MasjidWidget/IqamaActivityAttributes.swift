import ActivityKit
import Foundation

@available(iOS 26.0, *)
struct IqamaActivityAttributes: ActivityAttributes {
    struct ContentState: Codable, Hashable {
        var started: Bool
    }

    let id: String
    let prayer: String
    let countdownStart: Double
    let iqama: Double

    var countdownDate: Date { Date(timeIntervalSince1970: countdownStart) }
    var iqamaDate: Date { Date(timeIntervalSince1970: iqama) }
    var endDate: Date { iqamaDate.addingTimeInterval(60) }
}

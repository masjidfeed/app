import ActivityKit
import SwiftUI
import WidgetKit

@available(iOSApplicationExtension 26.0, *)
struct IqamaLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: IqamaActivityAttributes.self) { context in
            HStack {
                prayerLabel(context).font(.headline)
                Spacer()
                status(context)
            }
            .padding()
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    prayerLabel(context).font(.headline)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    status(context, ringSize: 42)
                }
            } compactLeading: {
                prayerLabel(context)
                    .font(.system(size: 13, weight: .semibold))
                    .lineLimit(1)
                    .allowsTightening(true)
                    .minimumScaleFactor(0.65)
            } compactTrailing: {
                status(context, ringSize: 32)
            } minimal: {
                if hasStarted(context) {
                    Image(systemName: "checkmark").accessibilityLabel("Iqama started")
                } else if Date.now >= context.attributes.endDate {
                    Image(systemName: "clock").accessibilityLabel("Iqama time passed")
                } else {
                    countdownRing(context, size: 24)
                        .accessibilityLabel("\(context.attributes.prayer) iqama")
                        .accessibilityValue(countdown(context))
                }
            }
        }
    }

    private func prayerLabel(_ context: ActivityViewContext<IqamaActivityAttributes>) -> Text {
        if hasStarted(context) {
            return Text(context.attributes.prayer)
        }
        return Text("\(context.attributes.prayer) iqama")
    }

    private func hasStarted(_ context: ActivityViewContext<IqamaActivityAttributes>) -> Bool {
        Date.now < context.attributes.endDate && (context.state.started || context.isStale)
    }

    @ViewBuilder
    private func status(_ context: ActivityViewContext<IqamaActivityAttributes>, ringSize: CGFloat? = nil) -> some View {
        if hasStarted(context) {
            Text("started")
        } else if Date.now >= context.attributes.endDate {
            Text("Time passed")
        } else if let ringSize {
            ZStack {
                countdownRing(context, size: ringSize)
                    .accessibilityHidden(true)
                countdown(context)
                    .font(.system(size: ringSize * 0.28, weight: .semibold))
                    .multilineTextAlignment(.center)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
                    .frame(width: ringSize - 7)
            }
        } else {
            countdown(context)
                .multilineTextAlignment(.trailing)
        }
    }

    private func countdown(_ context: ActivityViewContext<IqamaActivityAttributes>) -> Text {
        Text(timerInterval: context.attributes.countdownDate...context.attributes.iqamaDate,
             countsDown: true, showsHours: false)
            .monospacedDigit()
    }

    private func countdownRing(_ context: ActivityViewContext<IqamaActivityAttributes>, size: CGFloat) -> some View {
        ProgressView(timerInterval: context.attributes.countdownDate...context.attributes.iqamaDate, countsDown: true) {
            EmptyView()
        } currentValueLabel: {
            EmptyView()
        }
        .progressViewStyle(.circular)
        .frame(width: size, height: size)
    }
}

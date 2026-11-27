import ActivityKit
import SwiftUI
import WidgetKit

@available(iOSApplicationExtension 26.0, *)
struct IqamaLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: IqamaActivityAttributes.self) { context in
            HStack {
                VStack(alignment: .leading) {
                    Text(context.attributes.prayer).font(.headline)
                    Text("Iqama").font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
                status(context)
            }
            .padding()
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Text(context.attributes.prayer).font(.headline)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    status(context)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    Text("Iqama").font(.subheadline).foregroundStyle(.secondary)
                }
            } compactLeading: {
                Text(context.attributes.prayer)
                    .lineLimit(1)
            } compactTrailing: {
                status(context)
            } minimal: {
                if hasStarted(context) {
                    Image(systemName: "checkmark").accessibilityLabel("Iqama started")
                } else if Date.now >= context.attributes.endDate {
                    Image(systemName: "clock").accessibilityLabel("Iqama time passed")
                } else {
                    countdown(context, width: 36)
                }
            }
        }
    }

    private func hasStarted(_ context: ActivityViewContext<IqamaActivityAttributes>) -> Bool {
        Date.now < context.attributes.endDate && (context.state.started || context.isStale)
    }

    @ViewBuilder
    private func status(_ context: ActivityViewContext<IqamaActivityAttributes>) -> some View {
        if hasStarted(context) {
            Text("started")
        } else if Date.now >= context.attributes.endDate {
            Text("Time passed")
        } else {
            countdown(context, width: 48)
        }
    }

    private func countdown(_ context: ActivityViewContext<IqamaActivityAttributes>, width: CGFloat) -> some View {
        Text(timerInterval: context.attributes.countdownDate...context.attributes.iqamaDate,
             countsDown: true, showsHours: false)
            .monospacedDigit()
            .multilineTextAlignment(.trailing)
            .lineLimit(1)
            .minimumScaleFactor(0.7)
            .frame(width: width, alignment: .trailing)
    }
}

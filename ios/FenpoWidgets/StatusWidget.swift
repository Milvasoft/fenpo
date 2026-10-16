import FenpoApple
import FenpoCore
import SwiftUI
import WidgetKit

/// Timeline entry for every Fenpo widget: the status the app last wrote to the App Group.
struct StatusEntry: TimelineEntry {
    let date: Date
    let status: SharedStatus
}

/// Reads `SharedStatus`; the app reloads the timelines whenever it changes.
struct StatusProvider: TimelineProvider {
    func placeholder(in context: Context) -> StatusEntry {
        StatusEntry(date: .now, status: .sample)
    }

    func getSnapshot(in context: Context, completion: @escaping (StatusEntry) -> Void) {
        let status = context.isPreview ? SharedStatus.sample : SharedStatusStore().load()
        completion(StatusEntry(date: .now, status: status))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<StatusEntry>) -> Void) {
        completion(Timeline(entries: [StatusEntry(date: .now, status: SharedStatusStore().load())], policy: .never))
    }
}

/// The paired PC, whether it is connected and the last transfer (Lock Screen rectangle, Home Screen small).
struct StatusWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "com.fenpo.status", provider: StatusProvider()) { entry in
            StatusWidgetView(status: entry.status)
                .containerBackground(.fill.tertiary, for: .widget)
        }
        .configurationDisplayName("widget.status.name")
        .description("widget.status.description")
        .supportedFamilies([.systemSmall, .accessoryRectangular])
    }
}

struct StatusWidgetView: View {
    let status: SharedStatus

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            if let pcName = status.pcName {
                Text(verbatim: pcName)
                    .font(.headline)
                    .widgetAccentable()
                Text(status.isConnected ? "widget.status.connected" : "widget.status.disconnected")
                    .font(.caption)
                if let last = status.lastTransferName {
                    Text("widget.status.last \(last)")
                        .font(.caption)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
            } else {
                Text("widget.status.notPaired")
                    .font(.caption)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// The PC's battery as a ring (Lock Screen circle). Shows the PC symbol when the PC reports none.
struct PCBatteryWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "com.fenpo.pcBattery", provider: StatusProvider()) { entry in
            PCBatteryView(status: entry.status)
                .containerBackground(.fill.tertiary, for: .widget)
        }
        .configurationDisplayName("widget.battery.name")
        .description("widget.battery.description")
        .supportedFamilies([.accessoryCircular])
    }
}

struct PCBatteryView: View {
    let status: SharedStatus

    var body: some View {
        if let percent = status.pcBatteryPercent, status.isConnected {
            Gauge(value: Double(percent), in: 0...100) {
                Text("widget.battery.label")
            } currentValueLabel: {
                Text(Double(percent) / 100, format: .percent.precision(.fractionLength(0)))
            }
            .gaugeStyle(.accessoryCircular)
            .widgetAccentable()
        } else {
            ZStack {
                AccessoryWidgetBackground()
                Image(systemName: status.isConnected ? "laptopcomputer" : "laptopcomputer.slash")
                    .font(.title3)
            }
            .accessibilityLabel(Text(status.isConnected ? "widget.status.connected" : "widget.status.disconnected"))
        }
    }
}

/// One tap "Paste from PC" on the Lock Screen.
struct PasteWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "com.fenpo.paste", provider: StatusProvider()) { _ in
            Button(intent: PasteFromPCIntent()) {
                ZStack {
                    AccessoryWidgetBackground()
                    Image(systemName: "arrow.down.doc")
                        .font(.title3)
                }
            }
            .buttonStyle(.plain)
            .accessibilityLabel(Text("control.pasteFromPC"))
            .containerBackground(.fill.tertiary, for: .widget)
        }
        .configurationDisplayName("control.pasteFromPC")
        .description("control.pasteFromPC.description")
        .supportedFamilies([.accessoryCircular])
    }
}

#Preview("Status", as: .accessoryRectangular) {
    StatusWidget()
} timeline: {
    StatusEntry(date: .now, status: .sample)
    StatusEntry(date: .now, status: .unpaired)
}

#Preview("Battery", as: .accessoryCircular) {
    PCBatteryWidget()
} timeline: {
    StatusEntry(date: .now, status: .sample)
}

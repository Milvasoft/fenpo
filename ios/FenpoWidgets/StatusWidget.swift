import SwiftUI
import WidgetKit

/// Shows the paired PC and whether it is connected.
struct StatusWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "com.fenpo.status", provider: StatusProvider()) { entry in
            StatusWidgetView(entry: entry)
                .containerBackground(.fill.tertiary, for: .widget)
        }
        .configurationDisplayName("widget.status.name")
        .description("widget.status.description")
        .supportedFamilies([.systemSmall, .accessoryRectangular])
    }
}

struct StatusEntry: TimelineEntry {
    let date: Date
    let pcName: String?
    let connected: Bool

    static let sample = StatusEntry(date: .now, pcName: "BUGRA-PC", connected: true)
    static let notPaired = StatusEntry(date: .now, pcName: nil, connected: false)
}

/// Reads nothing yet; the shared pairing state arrives with the channel work.
struct StatusProvider: TimelineProvider {
    func placeholder(in context: Context) -> StatusEntry {
        .sample
    }

    func getSnapshot(in context: Context, completion: @escaping (StatusEntry) -> Void) {
        completion(context.isPreview ? .sample : .notPaired)
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<StatusEntry>) -> Void) {
        completion(Timeline(entries: [.notPaired], policy: .never))
    }
}

struct StatusWidgetView: View {
    let entry: StatusEntry

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let pcName = entry.pcName {
                Text(pcName)
                    .font(.headline)
                Text(entry.connected ? "widget.status.connected" : "widget.status.disconnected")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Text("widget.status.notPaired")
                    .font(.caption)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

#Preview("Small", as: .systemSmall) {
    StatusWidget()
} timeline: {
    StatusEntry.sample
    StatusEntry.notPaired
}

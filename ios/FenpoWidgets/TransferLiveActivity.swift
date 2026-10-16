import ActivityKit
import SwiftUI
import WidgetKit

/// Large transfers on the Lock Screen and in the Dynamic Island (design: "Kilit ekranı").
struct TransferLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: TransferActivityAttributes.self) { context in
            TransferLockScreenView(attributes: context.attributes, state: context.state)
                .activityBackgroundTint(.ink)
                .activitySystemActionForegroundColor(.onAccent)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    FenpoMark()
                        .frame(width: 30, height: 30)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    TransferPercent(state: context.state)
                        .font(.headline)
                }
                DynamicIslandExpandedRegion(.center) {
                    TransferTitle(attributes: context.attributes)
                        .font(.headline)
                        .lineLimit(1)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    VStack(alignment: .leading, spacing: 6) {
                        TransferProgressBar(state: context.state)
                        TransferSubtitle(attributes: context.attributes, state: context.state)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            } compactLeading: {
                FenpoMark()
                    .frame(width: 22, height: 22)
            } compactTrailing: {
                TransferPercent(state: context.state)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.blush)
            } minimal: {
                ProgressView(value: context.state.fraction)
                    .progressViewStyle(.circular)
                    .tint(.blush)
            }
            .keylineTint(.bloom)
        }
    }
}

/// The Lock Screen banner: logo, title, files left, percent and the bar.
private struct TransferLockScreenView: View {
    let attributes: TransferActivityAttributes
    let state: TransferActivityAttributes.ContentState

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                FenpoMark()
                    .frame(width: 30, height: 30)
                VStack(alignment: .leading, spacing: 1) {
                    TransferTitle(attributes: attributes)
                        .font(.headline)
                        .lineLimit(1)
                    TransferSubtitle(attributes: attributes, state: state)
                        .font(.footnote)
                        .opacity(0.75)
                }
                Spacer(minLength: 8)
                TransferPercent(state: state)
                    .font(.headline)
            }
            TransferProgressBar(state: state)
            Text("live.wifi.pc \(attributes.pcName)")
                .font(.caption)
                .opacity(0.7)
        }
        .foregroundStyle(.onAccent)
        .padding(.horizontal, 18)
        .padding(.vertical, 16)
    }
}

private struct TransferTitle: View {
    let attributes: TransferActivityAttributes

    var body: some View {
        Text(attributes.isSending ? "live.sending" : "live.receiving")
    }
}

private struct TransferSubtitle: View {
    let attributes: TransferActivityAttributes
    let state: TransferActivityAttributes.ContentState

    var body: some View {
        if attributes.fileCount > 1 {
            Text("live.files \(attributes.fileCount) \(state.filesLeft)")
        } else {
            Text("live.file")
        }
    }
}

private struct TransferPercent: View {
    let state: TransferActivityAttributes.ContentState

    var body: some View {
        Text(state.fraction, format: .percent.precision(.fractionLength(0)))
            .monospacedDigit()
    }
}

private struct TransferProgressBar: View {
    let state: TransferActivityAttributes.ContentState

    var body: some View {
        ProgressView(value: state.fraction)
            .progressViewStyle(.linear)
            .tint(.blush)
    }
}

#Preview("Lock Screen", as: .content, using: TransferActivityAttributes(pcName: "BUGRA-PC", fileCount: 12, isSending: true)) {
    TransferLiveActivity()
} contentStates: {
    TransferActivityAttributes.ContentState(bytesDone: 64, totalBytes: 100, filesLeft: 3)
}

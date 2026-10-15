import FenpoCore
import SwiftUI

/// "Almost done": data channel, Bluetooth bond and notification sharing, each with its state.
struct SetupStepsView: View {
    let pcName: String
    let progress: PairingProgress
    let onConnectBluetooth: () -> Void
    let onRetry: (PairingProgress.Step) -> Void
    let onDone: () -> Void

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                CardRows(separatorInset: 64) {
                    StepRow(icon: .wifi, color: .leaf, title: "pairing.step.wifi", subtitle: subtitle(for: .dataChannel), state: progress.state(of: .dataChannel))
                    StepRow(icon: .bluetooth, color: .blue, title: "pairing.step.bluetooth", subtitle: subtitle(for: .bluetooth), state: progress.state(of: .bluetooth))
                    StepRow(icon: .bell, color: .slate, title: "pairing.step.notifications", subtitle: subtitle(for: .notifications), state: progress.state(of: .notifications))
                }
                .padding(.top, 8)

                Spacer(minLength: 24)

                bottomAction
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 16)
            .fenpoScreenBackground()
            .navigationTitle("pairing.almostDone")
        }
    }

    @ViewBuilder
    private var bottomAction: some View {
        if let step = progress.current {
            switch (step, progress.state(of: step)) {
            case (_, .failed):
                Button("common.retry") {
                    onRetry(step)
                }
                .buttonStyle(PrimaryPillButtonStyle())
            case (.bluetooth, _):
                Button("pairing.connectBluetooth", action: onConnectBluetooth)
                    .buttonStyle(PrimaryPillButtonStyle())
            case (.notifications, _):
                Text("pairing.notifications.hint")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity)
            case (.dataChannel, _):
                EmptyView()
            }
        } else {
            Button("pairing.done", action: onDone)
                .buttonStyle(PrimaryPillButtonStyle())
        }
    }

    private func subtitle(for step: PairingProgress.Step) -> Text {
        switch (step, progress.state(of: step)) {
        case (_, .queued):
            Text("pairing.step.queued")
        case (_, .failed):
            Text("pairing.step.failed")
        case (.dataChannel, .inProgress):
            Text("pairing.step.wifi.inProgress")
        case (.dataChannel, .done):
            Text("pairing.step.wifi.done \(pcName)")
        case (.bluetooth, .inProgress):
            Text("pairing.step.bluetooth.inProgress")
        case (.bluetooth, .done):
            Text("pairing.step.bluetooth.done")
        case (.notifications, .inProgress):
            Text("pairing.step.notifications.inProgress")
        case (.notifications, .done):
            Text("pairing.step.notifications.done")
        }
    }
}

/// One pairing step: 36 pt icon, title, state text and a trailing state mark.
struct StepRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    let subtitle: Text
    let state: PairingProgress.State

    var body: some View {
        HStack(spacing: 12) {
            IconBadge(icon: icon, color: color, size: 36, cornerRadius: 9)
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                    .font(.body.weight(.medium))
                    .foregroundStyle(.textPrimary)
                subtitle
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
            }
            Spacer(minLength: 0)
            switch state {
            case .done:
                Image(systemName: "checkmark")
                    .font(.body.weight(.semibold))
                    .foregroundStyle(.green)
            case .inProgress:
                ProgressView()
            case .failed:
                Image(systemName: "exclamationmark.circle")
                    .foregroundStyle(.red)
            case .queued:
                EmptyView()
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .frame(minHeight: 52)
        .accessibilityElement(children: .combine)
    }
}

private extension PairingProgress {
    static var waitingForBluetooth: PairingProgress {
        var progress = PairingProgress()
        progress.complete(.dataChannel)
        return progress
    }
}

#Preview("Light · Türkçe") {
    SetupStepsView(pcName: "BUGRA-PC", progress: .waitingForBluetooth, onConnectBluetooth: {}, onRetry: { _ in }, onDone: {})
        .preview(.light, "tr")
}

#Preview("Dark · English") {
    SetupStepsView(pcName: "BUGRA-PC", progress: .waitingForBluetooth, onConnectBluetooth: {}, onRetry: { _ in }, onDone: {})
        .preview(.dark, "en")
}

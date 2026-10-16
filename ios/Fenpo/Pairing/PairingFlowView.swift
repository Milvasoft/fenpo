import FenpoApple
import FenpoCore
import SwiftUI

/// Pairing from start to end: scan the QR code (or type the code), then the setup steps.
struct PairingFlowView: View {
    private enum Stage {
        case scanning
        case setup
    }

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    @State private var stage: Stage = .scanning
    @State private var isManualEntryPresented = false
    @State private var progress = PairingProgress()
    @State private var pcName = ""
    @State private var input: PairingInput?

    private let log = FenpoLog("Pairing")

    var body: some View {
        content
            .onAppear(perform: openScreenshotScene)
    }

    @ViewBuilder
    private var content: some View {
        switch stage {
        case .scanning:
            ScannerView(
                onCode: { start(.qrCode($0)) },
                onManualEntry: { isManualEntryPresented = true },
                onClose: { dismiss() }
            )
            .sheet(isPresented: $isManualEntryPresented) {
                ManualCodeView { code in
                    isManualEntryPresented = false
                    start(.manualCode(code))
                }
            }
        case .setup:
            SetupStepsView(
                pcName: pcName,
                progress: progress,
                onConnectBluetooth: connectBluetooth,
                onRetry: retry,
                onDone: {
                    Task { await model.pairingFinished() }
                }
            )
        }
    }

    private func openScreenshotScene() {
        #if DEBUG
        switch ScreenshotScene.current {
        case .code:
            isManualEntryPresented = true
        case .steps:
            pcName = "BUGRA-PC"
            progress.complete(.dataChannel)
            stage = .setup
        default:
            break
        }
        #endif
    }

    private func start(_ input: PairingInput) {
        self.input = input
        stage = .setup
        runDataChannel()
    }

    private func runDataChannel() {
        guard let input else {
            return
        }
        Task {
            do {
                let pc = try await model.pairing.startDataChannel(input)
                pcName = pc.name
                progress.complete(.dataChannel)
            } catch {
                log.error("Data channel failed: \(error.localizedDescription)")
                progress.fail(.dataChannel)
            }
        }
    }

    private func connectBluetooth() {
        Task {
            do {
                try await model.pairing.bondBluetooth()
                progress.complete(.bluetooth)
            } catch {
                log.error("Bluetooth bond failed: \(error.localizedDescription)")
                progress.fail(.bluetooth)
                return
            }
            do {
                try await model.pairing.waitForNotificationSharing()
                progress.complete(.notifications)
            } catch {
                log.error("Notification sharing failed: \(error.localizedDescription)")
                progress.fail(.notifications)
            }
        }
    }

    private func retry(_ step: PairingProgress.Step) {
        progress.retry(step)
        switch step {
        case .dataChannel:
            runDataChannel()
        case .bluetooth, .notifications:
            connectBluetooth()
        }
    }
}

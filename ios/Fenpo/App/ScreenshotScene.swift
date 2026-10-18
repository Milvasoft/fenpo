#if DEBUG
import FenpoCore
import Foundation
import SwiftUI

/// Screens CI opens directly to take simulator screenshots (`-fenpo.screenshot <name>`, see ios/scripts/screenshots.sh).
enum ScreenshotScene: String {
    case home
    case homeUnpaired = "home-unpaired"
    /// Home after the Bluetooth link turned out to reach another PC.
    case homeBluetoothOtherPC = "home-bluetooth-other-pc"
    case transfers
    case pc
    case settings
    case scan
    case developer
    case steps
    case ringing
    case camera
    case touchpad
    case presenter
    case transferDetail = "transfer-detail"
    case cameraReady = "camera-ready"
    /// The App Store preview: the screens one after the other, recorded from the simulator.
    case demo

    /// The scene requested by the launch arguments, if any.
    static var current: ScreenshotScene? {
        UserDefaults.standard.string(forKey: "fenpo.screenshot").flatMap(ScreenshotScene.init(rawValue:))
    }

    /// A model that opens this scene.
    @MainActor
    func makeModel() -> AppModel {
        let model = AppModel(session: makeSession())
        switch self {
        case .home, .homeUnpaired, .homeBluetoothOtherPC:
            model.selectedTab = .home
        case .transfers:
            model.selectedTab = .transfers
        case .pc:
            model.selectedTab = .pc
        case .settings, .developer:
            model.selectedTab = .settings
        case .scan, .steps:
            model.isPairingPresented = true
        case .ringing:
            model.isRinging = true
        case .camera:
            model.cameraState = .sample
        case .touchpad:
            model.selectedTab = .pc
            model.pcPath = [.touchpad]
        case .presenter:
            model.selectedTab = .pc
            model.pcPath = [.presenter]
        case .cameraReady:
            model.isCameraReadyPresented = true
        case .demo:
            model.selectedTab = .home
            Self.runDemoTour(model)
        case .transferDetail:
            model.selectedTab = .transfers
            if let first = SampleSession.pairedSnapshot().transfers.first {
                model.transfersPath = [TransferRoute(id: first.id)]
            }
        }
        return model
    }

    private func makeSession() -> SampleSession {
        switch self {
        case .homeUnpaired:
            return SampleSession(paired: false)
        case .homeBluetoothOtherPC:
            var snapshot = SampleSession.pairedSnapshot()
            snapshot.links.bluetooth = false
            snapshot.bluetoothNeedsPairing = true
            return SampleSession(snapshot: snapshot)
        default:
            return SampleSession()
        }
    }

    /// Seconds each screen of the demo tour stays; the App Store preview's captions follow the same
    /// steps (design/store/scripts).
    static let demoStep: Duration = .milliseconds(3_500)

    /// Home, Transfers, the webcam screen, the PC tab, the touchpad, the presentation remote and
    /// Settings, `demoStep` each. Home stays longer (3 s more) so the preview's poster frame at 5 s
    /// still shows it.
    @MainActor
    private static func runDemoTour(_ model: AppModel) {
        let steps: [@MainActor (AppModel) -> Void] = [
            { $0.selectedTab = .transfers },
            { $0.isCameraReadyPresented = true },
            { model in
                model.isCameraReadyPresented = false
                model.selectedTab = .pc
            },
            { $0.pcPath = [.touchpad] },
            { $0.pcPath = [.presenter] },
            { model in
                model.pcPath = []
                model.selectedTab = .settings
            },
        ]
        Task { @MainActor in
            try? await Task.sleep(for: .seconds(3))
            for step in steps {
                try? await Task.sleep(for: demoStep)
                withAnimation(.snappy) {
                    step(model)
                }
            }
        }
    }
}
#endif

#if DEBUG
import FenpoCore
import Foundation

/// Screens CI opens directly to take simulator screenshots (`-fenpo.screenshot <name>`, see ios/scripts/screenshots.sh).
enum ScreenshotScene: String {
    case home
    case homeUnpaired = "home-unpaired"
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

    /// The scene requested by the launch arguments, if any.
    static var current: ScreenshotScene? {
        UserDefaults.standard.string(forKey: "fenpo.screenshot").flatMap(ScreenshotScene.init(rawValue:))
    }

    /// A model that opens this scene.
    @MainActor
    func makeModel() -> AppModel {
        let model = AppModel(session: SampleSession(paired: self != .homeUnpaired))
        switch self {
        case .home, .homeUnpaired:
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
        }
        return model
    }
}
#endif

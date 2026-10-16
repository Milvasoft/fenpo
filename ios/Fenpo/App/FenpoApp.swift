import FenpoApple
import FenpoCore
import SwiftUI

@main
struct FenpoApp: App {
    @UIApplicationDelegateAdaptor(FenpoAppDelegate.self) private var appDelegate
    @Environment(\.scenePhase) private var scenePhase
    @State private var appearance = AppearanceStore()
    @State private var model = FenpoApp.makeModel()

    private let log = FenpoLog("App")

    /// The real services, or `nil` in screenshot scenes.
    @MainActor private static var services: FenpoServices?

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(appearance)
                .environment(model)
                .preferredColorScheme(appearance.preference.colorScheme)
                .task {
                    await model.refresh()
                }
        }
        .onChange(of: scenePhase) { _, phase in
            log.info("Scene \(String(describing: phase))")
            if phase == .active {
                FenpoApp.services?.appBecameActive()
                Task { await model.becameActive() }
            } else if phase == .background {
                FenpoApp.services?.appEnteredBackground()
            }
        }
    }

    /// Screenshot scenes use sample data; a normal launch starts the real channels.
    private static func makeModel() -> AppModel {
        #if DEBUG
        if let scene = ScreenshotScene.current {
            return scene.makeModel()
        }
        #endif
        let services = FenpoServices.shared
        FenpoApp.services = services
        let model = AppModel(session: services.session, pairing: services.pairing, actions: services.actions, ringer: services.ringer, camera: services.camera, transferActivities: TransferActivities(), readsPermissions: true)
        services.onChange = {
            Task { await model.refresh() }
        }
        services.ringer.onChange = { isRinging in
            model.isRinging = isRinging
        }
        services.start()
        return model
    }
}

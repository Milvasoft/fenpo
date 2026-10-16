import FenpoApple
import FenpoCore
import SwiftUI

@main
struct FenpoApp: App {
    @State private var appearance = AppearanceStore()
    @State private var model = FenpoApp.makeModel()

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
    }

    /// Screenshot scenes use sample data; a normal launch starts the real channels.
    private static func makeModel() -> AppModel {
        #if DEBUG
        if let scene = ScreenshotScene.current {
            return scene.makeModel()
        }
        #endif
        let services = FenpoServices.shared
        let model = AppModel(session: services.session, pairing: services.pairing)
        services.central.onStateChange = { _ in
            Task { await model.refresh() }
        }
        services.start()
        return model
    }
}

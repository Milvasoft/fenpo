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

    private static func makeModel() -> AppModel {
        #if DEBUG
        if let scene = ScreenshotScene.current {
            return scene.makeModel()
        }
        #endif
        return AppModel(session: SampleSession())
    }
}

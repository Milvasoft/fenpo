import FenpoApple
import FenpoCore
import SwiftUI

@main
struct FenpoApp: App {
    @State private var appearance = AppearanceStore()
    @State private var model = AppModel(session: SampleSession())

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
}

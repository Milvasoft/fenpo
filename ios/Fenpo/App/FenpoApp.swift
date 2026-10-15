import FenpoApple
import SwiftUI

@main
struct FenpoApp: App {
    @State private var appearance = AppearanceStore()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(appearance)
                .preferredColorScheme(appearance.preference.colorScheme)
        }
    }
}

import FenpoApple
import FenpoCore
import SwiftUI

extension View {
    /// Applies a color scheme and an app language for previews.
    /// - Parameters:
    ///   - scheme: Light or dark.
    ///   - language: "tr" or "en".
    func preview(_ scheme: ColorScheme, _ language: String) -> some View {
        self
            .preferredColorScheme(scheme)
            .environment(\.locale, Locale(identifier: language))
    }

    /// Puts the view in an app model backed by sample data, with its own appearance store.
    /// - Parameter paired: Start with a paired PC.
    @MainActor
    func sampleModel(paired: Bool = true) -> some View {
        environment(AppModel.preview(paired: paired))
            .environment(AppearanceStore(defaults: UserDefaults(suiteName: "preview")))
    }
}

extension AppModel {
    /// A model already filled with sample data, so previews render without waiting.
    /// - Parameter paired: Start with a paired PC.
    static func preview(paired: Bool = true) -> AppModel {
        let model = AppModel(session: SampleSession(paired: paired))
        model.snapshot = paired ? SampleSession.pairedSnapshot() : .unpaired
        return model
    }
}

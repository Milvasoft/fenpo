import SwiftUI

/// Top level view. Replaced by the tab bar in the UI work block.
struct RootView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("root.placeholder.title", systemImage: "laptopcomputer.and.iphone")
            } description: {
                Text("root.placeholder.message")
            }
            .foregroundStyle(.textPrimary)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(.canvas)
            .navigationTitle("app.name")
        }
    }
}

#Preview("Light, English") {
    RootView()
        .preferredColorScheme(.light)
        .environment(\.locale, Locale(identifier: "en"))
}

#Preview("Dark, Türkçe") {
    RootView()
        .preferredColorScheme(.dark)
        .environment(\.locale, Locale(identifier: "tr"))
}

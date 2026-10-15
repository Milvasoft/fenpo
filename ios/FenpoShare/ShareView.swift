import SwiftUI

/// What the share extension shows. Sending to the PC arrives with the transfer work.
struct ShareView: View {
    let close: () -> Void

    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("share.placeholder.title", systemImage: "laptopcomputer")
            } description: {
                Text("share.placeholder.message")
            }
            .foregroundStyle(.textPrimary)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("share.close", action: close)
                }
            }
        }
    }
}

#Preview("Light, English") {
    ShareView {}
        .preferredColorScheme(.light)
        .environment(\.locale, Locale(identifier: "en"))
}

#Preview("Dark, Türkçe") {
    ShareView {}
        .preferredColorScheme(.dark)
        .environment(\.locale, Locale(identifier: "tr"))
}

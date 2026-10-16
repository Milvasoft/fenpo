import SwiftUI

/// A short result message above the tab bar.
struct ToastView: View {
    let toast: AppModel.Toast

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: toast.isError ? "exclamationmark.circle.fill" : "checkmark.circle.fill")
                .foregroundStyle(toast.isError ? Color.red : Color.leaf)
            Text(toast.message)
                .font(.subheadline)
                .foregroundStyle(.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .background(.card, in: .capsule)
        .shadow(color: Color.ink.opacity(0.18), radius: 16, y: 6)
        .padding(.horizontal, 24)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isStaticText)
    }
}

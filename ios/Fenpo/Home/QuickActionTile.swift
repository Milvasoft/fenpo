import SwiftUI

/// One of the four quick actions on the home screen.
struct QuickActionTile: View {
    let title: LocalizedStringKey
    let subtitle: Text
    let icon: FenpoIcon
    let color: Color
    let action: () -> Void

    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 0) {
                IconBadge(icon: icon, color: color, size: 34, cornerRadius: 11)
                Spacer(minLength: 8)
                Text(title)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.textPrimary)
                subtitle
                    .font(.caption)
                    .foregroundStyle(.textSecondary)
                    .lineLimit(1)
                    .padding(.top, 1)
            }
            .padding(14)
            .frame(maxWidth: .infinity, minHeight: 92, alignment: .leading)
            .fenpoCard(cornerRadius: 22)
            .opacity(isEnabled ? 1 : 0.5)
        }
        .buttonStyle(.plain)
    }
}

import SwiftUI

/// Something the user should fix, with how: a tinted card under the PC card.
struct NoticeBanner: View {
    let title: LocalizedStringKey
    let message: Text

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "exclamationmark.circle")
                .font(.subheadline)
                .foregroundStyle(.accent)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.subheadline.weight(.semibold))
                message
                    .font(.footnote)
            }
            .foregroundStyle(.textPrimary)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.bloom.opacity(0.1), in: .rect(cornerRadius: 18, style: .continuous))
    }
}

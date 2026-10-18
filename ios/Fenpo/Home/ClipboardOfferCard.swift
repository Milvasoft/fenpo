import FenpoApple
import SwiftUI

/// A clipboard copied while Fenpo was away. The system Paste button sends it with one tap and
/// without the iOS paste prompt; the content is never shown before that tap.
struct ClipboardOfferCard: View {
    let offer: AppModel.ClipboardOffer
    let send: ([NSItemProvider]) -> Void
    let dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 12) {
                IconBadge(icon: offer.kind == .image ? .photo : .clipboard, color: .bloom, size: 34, cornerRadius: 11)
                VStack(alignment: .leading, spacing: 2) {
                    Text("home.clipboardOffer.title")
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.textPrimary)
                    Text(message)
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Button(action: dismiss) {
                    Image(systemName: "xmark")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(.textTertiary)
                        .frame(width: 28, height: 28)
                        .contentShape(.rect)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(Text("home.clipboardOffer.dismiss"))
            }
            PasteButton(supportedContentTypes: PhoneClipboard.pasteTypes, payloadAction: send)
                .labelStyle(.titleAndIcon)
                .buttonBorderShape(.capsule)
                .tint(.bloom)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .fenpoCard(cornerRadius: 22)
    }

    private var message: LocalizedStringKey {
        if offer.isSensitive {
            return "home.clipboardOffer.sensitive"
        }
        switch offer.kind {
        case .text:
            return "home.clipboardOffer.text"
        case .link:
            return "home.clipboardOffer.link"
        case .image:
            return "home.clipboardOffer.image"
        }
    }
}

#Preview("Light · Türkçe") {
    ClipboardOfferCard(offer: .init(changeCount: 1, kind: .text, isSensitive: false), send: { _ in }, dismiss: {})
        .padding()
        .fenpoScreenBackground()
        .preferredColorScheme(.light)
        .environment(\.locale, Locale(identifier: "tr"))
}

#Preview("Dark · English, sensitive") {
    ClipboardOfferCard(offer: .init(changeCount: 1, kind: .text, isSensitive: true), send: { _ in }, dismiss: {})
        .padding()
        .fenpoScreenBackground()
        .preferredColorScheme(.dark)
        .environment(\.locale, Locale(identifier: "en"))
}

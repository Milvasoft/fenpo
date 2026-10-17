import SwiftUI

extension View {
    /// White (dark: graphite) card with the design's large corner radius.
    func fenpoCard(cornerRadius: CGFloat = 26) -> some View {
        background(.card, in: .rect(cornerRadius: cornerRadius, style: .continuous))
    }

    /// Screen background used behind every list and card.
    func fenpoScreenBackground() -> some View {
        frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .background(.canvas)
    }

    /// Liquid Glass on iOS 26, a thin material before that.
    @ViewBuilder
    /// - Parameters:
    ///   - shape: Shape of the glass.
    ///   - interactive: The glass reacts to touches. Off for a glass button's own label: interactive
    ///     glass there takes the tap and the button never fires (the camera screen's close button).
    func fenpoGlass<S: Shape>(in shape: S, interactive: Bool = true) -> some View {
        if #available(iOS 26, *) {
            glassEffect(interactive ? .regular.interactive() : .regular, in: shape)
        } else {
            background(.ultraThinMaterial, in: shape)
        }
    }
}

/// Small grey caption above a card, e.g. "Recent transfers".
struct SectionCaption: View {
    let title: LocalizedStringKey

    var body: some View {
        Text(title)
            .font(.footnote)
            .foregroundStyle(.textSecondary)
            .padding(.horizontal, 16)
            .padding(.top, 20)
            .padding(.bottom, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Rows inside a card, separated by a hairline that starts after the leading icon.
struct CardRows<Content: View>: View {
    var separatorInset: CGFloat = 58
    @ViewBuilder let content: Content

    var body: some View {
        VStack(spacing: 0) {
            Group(subviews: content) { subviews in
                ForEach(Array(subviews.enumerated()), id: \.offset) { index, subview in
                    if index > 0 {
                        Rectangle()
                            .fill(.hairline)
                            .frame(height: 1)
                            .padding(.leading, separatorInset)
                    }
                    subview
                }
            }
        }
        .fenpoCard()
        .clipShape(.rect(cornerRadius: 26, style: .continuous))
    }
}

/// Full width capsule button in the brand color.
struct PrimaryPillButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline)
            .foregroundStyle(.onAccent)
            .frame(maxWidth: .infinity, minHeight: 52)
            .background(.bloom, in: .capsule)
            .opacity(isEnabled ? (configuration.isPressed ? 0.8 : 1) : 0.4)
    }
}

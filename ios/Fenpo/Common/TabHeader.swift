import SwiftUI

extension View {
    /// The tab's large title with its button on the same line, like Mail and Notes, instead of the
    /// navigation bar's button above a large title. Pinned over the content; the navigation title
    /// stays set, so screens pushed from here show it on their back button.
    /// - Parameters:
    ///   - title: Tab title.
    ///   - trailing: Round button at the end of the line.
    func tabHeader<Trailing: View>(_ title: LocalizedStringKey, @ViewBuilder trailing: () -> Trailing) -> some View {
        modifier(TabHeader(title: title, trailing: trailing()))
    }

    /// The tab's large title alone, aligned like the tabs that have a button.
    /// - Parameter title: Tab title.
    func tabHeader(_ title: LocalizedStringKey) -> some View {
        modifier(TabHeader(title: title, trailing: EmptyView()))
    }
}

private struct TabHeader<Trailing: View>: ViewModifier {
    let title: LocalizedStringKey
    let trailing: Trailing

    func body(content: Content) -> some View {
        content
            .navigationTitle(title)
            .toolbar(.hidden, for: .navigationBar)
            .safeAreaInset(edge: .top, spacing: 0) {
                HStack(alignment: .center, spacing: 12) {
                    Text(title)
                        .font(.largeTitle.weight(.bold))
                        .foregroundStyle(.textPrimary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                        .accessibilityAddTraits(.isHeader)
                    Spacer(minLength: 0)
                    trailing
                }
                .frame(minHeight: 52)
                .padding(.horizontal, 16)
                .padding(.top, 4)
                .padding(.bottom, 6)
                .background(.canvas)
            }
    }
}

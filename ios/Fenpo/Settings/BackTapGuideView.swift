import SwiftUI

/// "Arkaya iki dokun": how to send the clipboard without opening Fenpo. iOS lets no app read the
/// pasteboard from the background, but the Shortcuts app can, so a shortcut made of "Get Clipboard"
/// and Fenpo's "Send text to PC" runs from Back Tap while Fenpo stays closed. iOS has no public link
/// to the Back Tap setting, so the steps name the path.
struct BackTapGuideView: View {
    @Environment(\.openURL) private var openURL

    private let steps: [LocalizedStringKey] = [
        "backTap.step.create",
        "backTap.step.getClipboard",
        "backTap.step.sendText",
        "backTap.step.name",
        "backTap.step.backTap",
    ]

    private let others: [(icon: FenpoIcon, title: LocalizedStringKey)] = [
        (.symbol("button.programmable"), "backTap.other.actionButton"),
        (.symbol("switch.2"), "backTap.other.controls"),
        (.symbol("square.and.arrow.up"), "backTap.other.share"),
        (.clipboard, "backTap.other.onOpen"),
    ]

    var body: some View {
        ScrollView {
            VStack(spacing: 0) {
                Text("backTap.explanation")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .padding(.horizontal, 16)
                    .padding(.top, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)

                SectionCaption(title: "backTap.section.steps")
                CardRows(separatorInset: 56) {
                    ForEach(Array(steps.enumerated()), id: \.offset) { index, step in
                        HStack(alignment: .center, spacing: 12) {
                            Text(verbatim: "\(index + 1)")
                                .font(.subheadline.weight(.semibold).monospacedDigit())
                                .foregroundStyle(.onAccent)
                                .frame(width: 28, height: 28)
                                .background(.bloom, in: .circle)
                            Text(step)
                                .foregroundStyle(.textPrimary)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .padding(.horizontal, 16)
                        .padding(.vertical, 12)
                    }
                }

                Button("backTap.openShortcuts") {
                    if let url = URL(string: "shortcuts://create-shortcut") {
                        openURL(url)
                    }
                }
                .buttonStyle(PrimaryPillButtonStyle())
                .padding(.top, 16)

                Text("backTap.firstRun")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .padding(.horizontal, 16)
                    .padding(.top, 10)
                    .frame(maxWidth: .infinity, alignment: .leading)

                SectionCaption(title: "backTap.section.other")
                CardRows(separatorInset: 58) {
                    ForEach(Array(others.enumerated()), id: \.offset) { _, other in
                        HStack(alignment: .top, spacing: 12) {
                            IconBadge(icon: other.icon, color: .ink)
                            Text(other.title)
                                .foregroundStyle(.textPrimary)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .padding(.horizontal, 16)
                        .padding(.vertical, 12)
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .fenpoScreenBackground()
        .navigationTitle("settings.backTap")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview("Light · Türkçe") {
    NavigationStack {
        BackTapGuideView()
    }
    .preferredColorScheme(.light)
    .environment(\.locale, Locale(identifier: "tr"))
}

#Preview("Dark · English") {
    NavigationStack {
        BackTapGuideView()
    }
    .preferredColorScheme(.dark)
    .environment(\.locale, Locale(identifier: "en"))
}

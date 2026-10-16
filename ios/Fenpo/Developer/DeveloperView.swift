import FenpoApple
import FenpoCore
import SwiftUI

/// Hidden menu for device tests (docs/ios/TESTING.md): channel state, test pairing with the
/// macOS test peripheral, Ping, reset and the log.
struct DeveloperView: View {
    @State private var diagnostics: FenpoServices.Diagnostics?
    @State private var result: Text?
    @State private var isWorking = false
    @State private var isConfirmingReset = false

    var body: some View {
        ScrollView {
            VStack(spacing: 0) {
                SectionCaption(title: "developer.section.status")
                CardRows(separatorInset: 16) {
                    InfoRow(title: "developer.bluetooth", value: diagnostics?.bluetoothState ?? "…")
                    InfoRow(title: "developer.peer", value: diagnostics?.peerName ?? "—")
                    InfoRow(title: "developer.capabilities", value: capabilitiesText)
                    InfoRow(title: "developer.pc", value: pcText)
                    InfoRow(title: "developer.accessory", value: diagnostics?.bluetoothIdentifier ?? "—")
                    InfoRow(title: "developer.counters", value: countersText)
                    InfoRow(title: "developer.version", value: diagnostics.map { "\($0.appVersion) (\($0.build))" } ?? "…")
                    InfoRow(title: "developer.deviceID", value: diagnostics?.deviceID ?? "…")
                }

                SectionCaption(title: "developer.section.tests")
                CardRows {
                    ActionRow(icon: .bluetooth, color: .blue, title: "developer.pairTest") {
                        run { try await FenpoServices.shared.pairWithTestPeripheral() } success: { Text("developer.result.paired") }
                    }
                    ActionRow(icon: .symbol("dot.radiowaves.left.and.right"), color: .dusk, title: "developer.ping") {
                        run { try FenpoServices.shared.sendPing() } success: { Text("developer.result.pingSent") }
                    }
                    ActionRow(icon: .symbol("arrow.counterclockwise"), color: .red, title: "developer.reset", role: .destructive) {
                        isConfirmingReset = true
                    }
                }
                .disabled(isWorking)

                if let result {
                    result
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                        .padding(.horizontal, 16)
                        .padding(.top, 8)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .textSelection(.enabled)
                }

                SectionCaption(title: "developer.section.logs")
                CardRows {
                    NavigationLink {
                        LogView()
                    } label: {
                        SettingsRowLayout(icon: .symbol("doc.text"), color: .slate, title: "developer.logs") {
                            Image(systemName: "chevron.right")
                                .font(.footnote.weight(.semibold))
                                .foregroundStyle(.textTertiary)
                        }
                        .contentShape(.rect)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .fenpoScreenBackground()
        .navigationTitle("developer.title")
        .navigationBarTitleDisplayMode(.inline)
        .confirmationDialog("developer.reset.confirm", isPresented: $isConfirmingReset, titleVisibility: .visible) {
            Button("developer.reset", role: .destructive) {
                run { try await FenpoServices.shared.resetEverything() } success: { Text("developer.result.reset") }
            }
        }
        .task {
            while !Task.isCancelled {
                diagnostics = FenpoServices.shared.diagnostics()
                try? await Task.sleep(for: .seconds(1))
            }
        }
    }

    private var pcText: String {
        guard let name = diagnostics?.pcName else {
            return "—"
        }
        return "\(name) · \(diagnostics?.pcID ?? "")"
    }

    private var capabilitiesText: String {
        guard let capabilities = diagnostics?.capabilities, !capabilities.isEmpty else {
            return "—"
        }
        return capabilities.joined(separator: " ")
    }

    private var countersText: String {
        guard let counters = diagnostics?.counters else {
            return "—"
        }
        return "↑ \(counters.lastSent)  ↓ \(counters.lastReceived)"
    }

    private func run(_ action: @escaping @MainActor () async throws -> Void, success: @escaping () -> Text) {
        isWorking = true
        result = Text("developer.result.working")
        Task {
            do {
                try await action()
                result = success()
            } catch {
                result = Text("developer.result.failed \(String(describing: error))")
            }
            isWorking = false
        }
    }
}

/// A label and a technical value that can be selected and copied.
struct InfoRow: View {
    let title: LocalizedStringKey
    let value: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(title)
                .foregroundStyle(.textPrimary)
            Spacer(minLength: 8)
            Text(verbatim: value)
                .font(.footnote.monospaced())
                .foregroundStyle(.textSecondary)
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .frame(minHeight: 50)
    }
}

#Preview("Light · Türkçe") {
    NavigationStack { DeveloperView() }.preview(.light, "tr")
}

#Preview("Dark · English") {
    NavigationStack { DeveloperView() }.preview(.dark, "en")
}

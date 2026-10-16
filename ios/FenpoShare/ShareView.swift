import FenpoCore
import SwiftUI

/// The share extension: "Open on PC" for links, "Send to PC" for files and photos. Both go over Wi-Fi.
struct ShareView: View {
    @State var model: ShareModel
    let close: () -> Void

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                if let pcName = model.pcName {
                    HStack(spacing: 12) {
                        Image(systemName: "laptopcomputer")
                            .font(.title2)
                            .foregroundStyle(.onAccent)
                            .frame(width: 48, height: 48)
                            .background(.bloom, in: .rect(cornerRadius: 12, style: .continuous))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(verbatim: pcName)
                                .font(.headline)
                                .foregroundStyle(.textPrimary)
                            if let host = model.url?.host() {
                                Text(verbatim: host)
                                    .font(.footnote)
                                    .foregroundStyle(.textSecondary)
                                    .lineLimit(1)
                            }
                        }
                    }

                    if !model.files.isEmpty {
                        Button {
                            Task { await model.sendFiles() }
                        } label: {
                            HStack {
                                if model.state == .sending {
                                    ProgressView()
                                        .tint(.onAccent)
                                }
                                Text("share.sendFiles \(model.files.count)")
                            }
                        }
                        .buttonStyle(SharePillButtonStyle())
                        .disabled(model.state == .sending || model.state == .done)

                        if let progress = model.progress, model.state == .sending {
                            ProgressView(value: Double(progress.done), total: Double(max(progress.total, 1)))
                                .tint(.bloom)
                        }
                    }

                    if model.url != nil {
                        Button {
                            Task { await model.openOnPC() }
                        } label: {
                            HStack {
                                if model.state == .sending {
                                    ProgressView()
                                        .tint(.onAccent)
                                }
                                Text("share.openOnPC")
                            }
                        }
                        .buttonStyle(SharePillButtonStyle())
                        .disabled(model.state == .sending || model.state == .done)
                    }

                    status
                } else {
                    ContentUnavailableView {
                        Label("share.notPaired.title", systemImage: "laptopcomputer")
                    } description: {
                        Text("share.placeholder.message")
                    }
                    .foregroundStyle(.textPrimary)
                }
                Spacer(minLength: 0)
            }
            .padding(20)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(.canvas)
            .navigationTitle("Fenpo")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("share.close", action: close)
                }
            }
        }
        .task {
            await model.load()
        }
        .onChange(of: model.state) { _, state in
            if state == .done {
                Task {
                    try? await Task.sleep(for: .seconds(1))
                    close()
                }
            }
        }
    }

    @ViewBuilder
    private var status: some View {
        switch model.state {
        case .done:
            Label(model.files.isEmpty ? "share.openOnPC.done" : "share.sendFiles.done", systemImage: "checkmark.circle.fill")
                .foregroundStyle(.leaf)
        case .failed(let error):
            Text(error.shareMessage)
                .foregroundStyle(.red)
        case .idle, .sending:
            if model.url == nil && model.files.isEmpty && !model.isLoading {
                Text("share.nothing")
                    .foregroundStyle(.textSecondary)
            }
        }
    }
}

/// Full width capsule in the brand color.
struct SharePillButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline)
            .foregroundStyle(.onAccent)
            .frame(maxWidth: .infinity, minHeight: 52)
            .background(.bloom, in: .capsule)
            .opacity(isEnabled ? (configuration.isPressed ? 0.8 : 1) : 0.5)
    }
}

extension FenpoActionError {
    /// Message for the share extension, which reaches the PC over Wi-Fi only.
    var shareMessage: LocalizedStringKey {
        switch self {
        case .notPaired:
            "action.error.notPaired"
        case .notConnected:
            "share.error.wifi"
        case .unsupportedByPC:
            "action.error.unsupportedByPC"
        case .declinedByPC:
            "action.error.declinedByPC"
        case .transferFailed:
            "action.error.transferFailed"
        default:
            "share.error.generic"
        }
    }
}

#Preview("Light · Türkçe") {
    ShareView(model: ShareModel(items: [], pcName: "BUGRA-PC")) {}
        .preferredColorScheme(.light)
        .environment(\.locale, Locale(identifier: "tr"))
}

#Preview("Dark · English") {
    ShareView(model: ShareModel(items: [], pcName: nil)) {}
        .preferredColorScheme(.dark)
        .environment(\.locale, Locale(identifier: "en"))
}

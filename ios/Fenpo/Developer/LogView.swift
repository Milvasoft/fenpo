import FenpoApple
import SwiftUI

/// The shared log file, newest line at the bottom, with export and clear.
struct LogView: View {
    private static let maxLines = 2_000

    @State private var text = ""
    @State private var exportURL: URL?
    @State private var isConfirmingClear = false

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                Text(verbatim: text)
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundStyle(.textPrimary)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(16)
                Color.clear
                    .frame(height: 1)
                    .id("end")
            }
            .onChange(of: text) {
                proxy.scrollTo("end", anchor: .bottom)
            }
        }
        .overlay {
            if text.isEmpty {
                ContentUnavailableView("developer.logs.empty", systemImage: "doc.text")
                    .foregroundStyle(.textPrimary)
            }
        }
        .fenpoScreenBackground()
        .navigationTitle("developer.logs")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                if let exportURL {
                    ShareLink(item: exportURL) {
                        Label("developer.logs.export", systemImage: "square.and.arrow.up")
                    }
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button("developer.logs.clear", systemImage: "trash", role: .destructive) {
                    isConfirmingClear = true
                }
            }
        }
        .confirmationDialog("developer.logs.clear.confirm", isPresented: $isConfirmingClear, titleVisibility: .visible) {
            Button("developer.logs.clear", role: .destructive) {
                FenpoLog.file.clear()
                reload()
            }
        }
        .task {
            while !Task.isCancelled {
                reload()
                try? await Task.sleep(for: .seconds(2))
            }
        }
    }

    private func reload() {
        let all = FenpoLog.file.read()
        let lines = all.split(separator: "\n", omittingEmptySubsequences: true)
        let latest = lines.suffix(Self.maxLines).joined(separator: "\n")
        if latest != text {
            text = latest
        }

        // Export everything the file holds, not only what is shown.
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("fenpo-log.txt")
        if (try? all.write(to: url, atomically: true, encoding: .utf8)) != nil {
            exportURL = url
        }
    }
}

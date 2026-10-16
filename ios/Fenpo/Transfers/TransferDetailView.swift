import FenpoApple
import FenpoCore
import QuickLook
import SwiftUI

/// Where a transfer row leads, on Home and on Transfers.
struct TransferRoute: Hashable {
    let id: UUID
}

/// One transfer: what it was, which way it went, how it ended and where the file is, with Open and
/// Share for files that came from the PC, and a way to send again when it did not finish.
struct TransferDetailView: View {
    let recordID: UUID

    @Environment(AppModel.self) private var model
    @Environment(\.locale) private var locale
    @State private var previewURL: URL?

    var body: some View {
        Group {
            if let record = model.snapshot.transfers.first(where: { $0.id == recordID }) {
                content(record)
            } else {
                ContentUnavailableView("transfer.detail.gone", systemImage: "doc.questionmark")
                    .foregroundStyle(.textPrimary)
            }
        }
        .fenpoScreenBackground()
        .navigationTitle("transfer.detail.title")
        .navigationBarTitleDisplayMode(.inline)
        .quickLookPreview($previewURL)
    }

    private func content(_ record: TransferRecord) -> some View {
        ScrollView {
            VStack(spacing: 0) {
                VStack(spacing: 10) {
                    FileBadge(record: record)
                        .scaleEffect(1.6)
                        .frame(width: 58, height: 58)
                    Text(verbatim: record.fileName)
                        .font(.title3.weight(.semibold))
                        .foregroundStyle(.textPrimary)
                        .multilineTextAlignment(.center)
                        .lineLimit(3)
                        .truncationMode(.middle)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 20)

                CardRows(separatorInset: 16) {
                    DetailRow(title: "transfer.detail.status", value: status(record))
                    DetailRow(title: "transfer.detail.direction", value: Text(record.direction == .fromPC ? "transfer.detail.fromPC" : "transfer.detail.toPC"))
                    if let size = record.size {
                        DetailRow(title: "transfer.detail.size", value: Text(verbatim: Int64(size).formatted(.byteCount(style: .file).locale(locale))))
                    }
                    DetailRow(title: "transfer.detail.time", value: Text(verbatim: record.date.formatted(Date.FormatStyle(date: .abbreviated, time: .shortened).locale(locale))))
                    DetailRow(title: "transfer.detail.location", value: location(record))
                }

                actions(record)
                    .padding(.top, 20)
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
    }

    @ViewBuilder
    private func actions(_ record: TransferRecord) -> some View {
        if let url = localURL(record) {
            VStack(spacing: 12) {
                Button("transfer.detail.open") {
                    previewURL = url
                }
                .buttonStyle(PrimaryPillButtonStyle())
                ShareLink(item: url) {
                    Label("transfer.detail.share", systemImage: "square.and.arrow.up")
                        .font(.body.weight(.semibold))
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
            }
        } else if record.outcome != .completed, record.direction == .toPC {
            VStack(spacing: 10) {
                Text("transfer.detail.resend.hint")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Button("transfer.detail.resend") {
                    model.sendPhotos()
                }
                .buttonStyle(PrimaryPillButtonStyle())
                .disabled(model.snapshot.pc == nil)
            }
        } else if record.direction == .fromPC {
            Text("transfer.detail.removed")
                .font(.footnote)
                .foregroundStyle(.textSecondary)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func status(_ record: TransferRecord) -> Text {
        switch record.outcome {
        case .completed:
            Text("transfer.detail.completed")
        case .failed:
            Text("transfer.detail.failed")
        case .cancelled:
            Text("transfer.detail.cancelled")
        }
    }

    private func location(_ record: TransferRecord) -> Text {
        switch record.direction {
        case .fromPC:
            Text("transfer.detail.inFiles")
        case .toPC:
            if let folder = record.folder {
                Text(verbatim: folder)
            } else {
                Text("transfer.detail.pcDownloads")
            }
        }
    }

    /// The received file, if it is still in Fenpo's folder.
    private func localURL(_ record: TransferRecord) -> URL? {
        guard record.direction == .fromPC, record.outcome == .completed, let name = record.localFileName else {
            return nil
        }
        let url = TransferStore.inboxURL.appendingPathComponent(name)
        return FileManager.default.fileExists(atPath: url.path) ? url : nil
    }
}

/// A title on the left and a value on the right.
private struct DetailRow: View {
    let title: LocalizedStringKey
    let value: Text

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(title)
                .foregroundStyle(.textPrimary)
            Spacer(minLength: 8)
            value
                .foregroundStyle(.textSecondary)
                .multilineTextAlignment(.trailing)
        }
        .padding(.horizontal, 16)
        .frame(minHeight: 48)
        .accessibilityElement(children: .combine)
    }
}

#Preview("Light · Türkçe") {
    NavigationStack {
        TransferDetailView(recordID: SampleSession.pairedSnapshot().transfers[0].id)
    }
    .sampleModel()
    .preview(.light, "tr")
}

#Preview("Dark · English") {
    NavigationStack {
        TransferDetailView(recordID: SampleSession.pairedSnapshot().transfers[1].id)
    }
    .sampleModel()
    .preview(.dark, "en")
}

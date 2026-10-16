import FenpoApple
import FenpoCore
import SwiftUI

/// Transfers in progress, then every finished one, newest first. Received files open in Quick Look.
struct TransfersView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model

        NavigationStack(path: $model.transfersPath) {
            Group {
                if model.snapshot.transfers.isEmpty && model.snapshot.activeTransfers.isEmpty {
                    ContentUnavailableView {
                        Label("transfers.empty.title", systemImage: "arrow.up.arrow.down")
                    } description: {
                        Text("transfers.empty.message")
                    }
                    .foregroundStyle(.textPrimary)
                } else {
                    ScrollView {
                        VStack(spacing: 0) {
                            if !model.snapshot.activeTransfers.isEmpty {
                                SectionCaption(title: "transfers.section.active")
                                CardRows(separatorInset: 64) {
                                    ForEach(model.snapshot.activeTransfers) { transfer in
                                        ActiveTransferRow(transfer: transfer)
                                    }
                                }
                            }
                            if !model.snapshot.transfers.isEmpty {
                                SectionCaption(title: "transfers.section.done")
                                CardRows(separatorInset: 64) {
                                    ForEach(model.snapshot.transfers) { record in
                                        NavigationLink(value: TransferRoute(id: record.id)) {
                                            TransferRow(record: record)
                                                .contentShape(.rect)
                                        }
                                        .buttonStyle(.plain)
                                    }
                                }
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.bottom, 24)
                    }
                    .refreshable {
                        await model.refresh()
                    }
                }
            }
            .fenpoScreenBackground()
            .navigationDestination(for: TransferRoute.self) { route in
                TransferDetailView(recordID: route.id)
            }
            .tabHeader("tab.transfers") {
                GlassCircleButton(systemImage: "plus", label: "home.sendPhotos") {
                    model.sendPhotos()
                }
                .disabled(model.snapshot.pc == nil)
            }
        }
    }
}

/// A transfer still moving: file, direction, progress bar and sizes.
struct ActiveTransferRow: View {
    let transfer: ActiveTransfer

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: transfer.direction == .toPC ? "arrow.up" : "arrow.down")
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(.onAccent)
                .frame(width: 36, height: 36)
                .background(.dusk, in: .rect(cornerRadius: 9, style: .continuous))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 4) {
                title
                    .font(.body.weight(.medium))
                    .foregroundStyle(.textPrimary)
                    .lineLimit(1)
                    .truncationMode(.middle)
                ProgressView(value: transfer.fraction)
                    .tint(.bloom)
                Text("transfer.progress \(sizeText(transfer.bytesDone)) \(sizeText(transfer.totalBytes))")
                    .font(.caption)
                    .foregroundStyle(.textSecondary)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .accessibilityElement(children: .combine)
    }

    private var title: Text {
        if transfer.fileCount == 1 {
            Text(verbatim: transfer.firstFileName)
        } else {
            Text("transfer.files \(transfer.fileCount)")
        }
    }

    private func sizeText(_ bytes: UInt64) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file)
    }
}

#Preview("Light · Türkçe") {
    TransfersView().sampleModel().preview(.light, "tr")
}

#Preview("Dark · English") {
    TransfersView().sampleModel().preview(.dark, "en")
}

#Preview("Light · English, empty") {
    TransfersView().sampleModel(paired: false).preview(.light, "en")
}

#Preview("Dark · Türkçe, empty") {
    TransfersView().sampleModel(paired: false).preview(.dark, "tr")
}

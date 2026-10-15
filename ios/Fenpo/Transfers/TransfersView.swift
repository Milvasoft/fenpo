import FenpoCore
import SwiftUI

/// Every finished transfer, newest first.
struct TransfersView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        NavigationStack {
            Group {
                if model.snapshot.transfers.isEmpty {
                    ContentUnavailableView {
                        Label("transfers.empty.title", systemImage: "arrow.up.arrow.down")
                    } description: {
                        Text("transfers.empty.message")
                    }
                    .foregroundStyle(.textPrimary)
                } else {
                    ScrollView {
                        CardRows(separatorInset: 64) {
                            ForEach(model.snapshot.transfers) { record in
                                TransferRow(record: record)
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.vertical, 8)
                    }
                    .refreshable {
                        await model.refresh()
                    }
                }
            }
            .fenpoScreenBackground()
            .navigationTitle("tab.transfers")
        }
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

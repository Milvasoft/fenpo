import FenpoCore
import SwiftUI

/// A finished transfer: file type badge, name, where it went and when.
struct TransferRow: View {
    let record: TransferRecord

    @Environment(\.locale) private var locale

    var body: some View {
        HStack(spacing: 12) {
            FileBadge(record: record)
            VStack(alignment: .leading, spacing: 1) {
                Text(verbatim: record.fileName)
                    .font(.body.weight(.medium))
                    .foregroundStyle(.textPrimary)
                    .lineLimit(1)
                    .truncationMode(.middle)
                subtitle
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
            }
            Spacer(minLength: 0)
            Image(systemName: "chevron.right")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(.textTertiary)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .frame(minHeight: 52)
        .accessibilityElement(children: .combine)
    }

    private var subtitle: Text {
        let when = record.date.formatted(.relative(presentation: .named, unitsStyle: .abbreviated).locale(locale))
        switch (record.direction, record.folder) {
        case (.fromPC, _):
            return Text("transfer.fromPC \(when)")
        case (.toPC, .some(let folder)):
            return Text("transfer.toPC.folder \(folder)")
        case (.toPC, .none):
            return Text("transfer.toPC \(when)")
        }
    }
}

/// Rounded square with the file extension, colored by file kind.
struct FileBadge: View {
    let record: TransferRecord

    var body: some View {
        Text(verbatim: String(record.fileExtension.prefix(4)))
            .font(.system(size: 9, weight: .bold))
            .foregroundStyle(.onAccent)
            .frame(width: 36, height: 36)
            .background(color, in: .rect(cornerRadius: 9, style: .continuous))
            .accessibilityHidden(true)
    }

    private var color: Color {
        switch record.kind {
        case .pdf:
            .bloom
        case .image, .video, .document:
            .dusk
        case .spreadsheet:
            .leaf
        case .other:
            .slate
        }
    }
}

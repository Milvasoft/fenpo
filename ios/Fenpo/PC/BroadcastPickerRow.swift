import ReplayKit
import SwiftUI
import UIKit

/// "Ekranı PC'de göster": a row that opens the system's broadcast sheet with Fenpo's broadcast
/// already chosen. Only the user can start a screen broadcast on the iPhone.
///
/// The system picker draws its own record button and lays it out after Fenpo does, so hiding or
/// stretching that button left a stray icon over the row. The picker now sits behind the row, one
/// point wide and almost transparent, and the row's own button triggers it.
struct BroadcastPickerRow: View {
    @State private var handle = BroadcastPickerHandle()

    var body: some View {
        Button {
            handle.open()
        } label: {
            LinkRow(icon: .symbol("rectangle.on.rectangle"), color: .slate, title: "remote.screen")
        }
        .buttonStyle(.plain)
        .background(alignment: .leading) {
            BroadcastPicker(handle: handle)
                .frame(width: 1, height: 1)
                .opacity(0.02)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }
}

/// Lets the row press the picker's button.
@MainActor
final class BroadcastPickerHandle {
    weak var picker: RPSystemBroadcastPickerView?

    /// Opens the system broadcast sheet.
    func open() {
        guard let picker else {
            return
        }
        for case let button as UIButton in picker.subviews {
            button.sendActions(for: .touchUpInside)
            return
        }
    }
}

private struct BroadcastPicker: UIViewRepresentable {
    static let extensionIdentifier = "com.fenpo.app.broadcast"

    let handle: BroadcastPickerHandle

    func makeUIView(context: Context) -> RPSystemBroadcastPickerView {
        let picker = RPSystemBroadcastPickerView(frame: CGRect(x: 0, y: 0, width: 1, height: 1))
        picker.preferredExtension = Self.extensionIdentifier
        picker.showsMicrophoneButton = false
        handle.picker = picker
        return picker
    }

    func updateUIView(_ picker: RPSystemBroadcastPickerView, context: Context) {
        handle.picker = picker
    }
}

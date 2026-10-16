import ReplayKit
import SwiftUI
import UIKit

/// "Ekranı PC'de göster": a row that opens the system's broadcast picker with Fenpo's broadcast
/// already chosen. Only the user can start a screen broadcast on the iPhone.
struct BroadcastPickerRow: View {
    var body: some View {
        LinkRow(icon: .symbol("rectangle.on.rectangle"), color: .slate, title: "remote.screen")
            .overlay {
                BroadcastPicker()
            }
            .accessibilityElement(children: .combine)
            .accessibilityAddTraits(.isButton)
    }
}

/// The system picker button, stretched over the row and invisible, so the whole row opens it.
private struct BroadcastPicker: UIViewRepresentable {
    static let extensionIdentifier = "com.fenpo.app.broadcast"

    func makeUIView(context: Context) -> RPSystemBroadcastPickerView {
        let picker = RPSystemBroadcastPickerView(frame: .zero)
        picker.preferredExtension = Self.extensionIdentifier
        picker.showsMicrophoneButton = false
        hideButtonImage(in: picker)
        return picker
    }

    func updateUIView(_ picker: RPSystemBroadcastPickerView, context: Context) {
        hideButtonImage(in: picker)
    }

    private func hideButtonImage(in picker: RPSystemBroadcastPickerView) {
        for case let button as UIButton in picker.subviews {
            button.imageView?.alpha = 0
            button.frame = picker.bounds
            button.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        }
    }
}

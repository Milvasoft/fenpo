import AppIntents
import SwiftUI
import WidgetKit

/// Control Center, Lock Screen and Action button controls from the design: send the clipboard,
/// paste from the PC, lock the PC.
struct SendClipboardControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "com.fenpo.control.sendClipboard") {
            ControlWidgetButton(action: SendClipboardToPCIntent()) {
                Label("control.sendClipboard", systemImage: "doc.on.clipboard")
            }
            .tint(.bloom)
        }
        .displayName("control.sendClipboard")
        .description("control.sendClipboard.description")
    }
}

struct PasteFromPCControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "com.fenpo.control.pasteFromPC") {
            ControlWidgetButton(action: PasteFromPCIntent()) {
                Label("control.pasteFromPC", systemImage: "arrow.down.doc")
            }
            .tint(.bloom)
        }
        .displayName("control.pasteFromPC")
        .description("control.pasteFromPC.description")
    }
}

struct LockPCControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "com.fenpo.control.lockPC") {
            ControlWidgetButton(action: LockPCIntent()) {
                Label("control.lockPC", systemImage: "lock.display")
            }
            .tint(.bloom)
        }
        .displayName("control.lockPC")
        .description("control.lockPC.description")
    }
}

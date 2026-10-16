import SwiftUI
import WidgetKit

@main
struct FenpoWidgetsBundle: WidgetBundle {
    var body: some Widget {
        SendClipboardControl()
        PasteFromPCControl()
        LockPCControl()
        StatusWidget()
        PCBatteryWidget()
        PasteWidget()
    }
}

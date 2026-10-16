import FenpoApple
import Foundation
import ReplayKit

/// "Ekranı PC'de göster": a ReplayKit broadcast the user starts from Fenpo or from Control Center
/// (hold Screen Recording, pick Fenpo). Everything else is in `ScreenBroadcaster`.
final class SampleHandler: RPBroadcastSampleHandler {
    private lazy var broadcaster = ScreenBroadcaster { [weak self] reason in
        self?.end(reason)
    }

    override func broadcastStarted(withSetupInfo setupInfo: [String: NSObject]?) {
        broadcaster.start()
    }

    override func broadcastFinished() {
        broadcaster.finish()
    }

    override func processSampleBuffer(_ sampleBuffer: CMSampleBuffer, with sampleBufferType: RPSampleBufferType) {
        guard sampleBufferType == .video else {
            return
        }
        broadcaster.process(sampleBuffer)
    }

    /// Ends the broadcast with a message iOS shows to the user.
    private func end(_ reason: String) {
        let message: String
        switch reason {
        case "stopped from the PC":
            message = String(localized: "broadcast.stopped.pc")
        case "unsupported":
            message = String(localized: "broadcast.error.unsupported")
        default:
            message = String(localized: "broadcast.error.unreachable")
        }
        finishBroadcastWithError(NSError(domain: "com.fenpo.broadcast", code: 1, userInfo: [NSLocalizedDescriptionKey: message]))
    }
}

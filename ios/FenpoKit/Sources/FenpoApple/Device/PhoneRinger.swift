#if os(iOS)
import AVFoundation
import FenpoProtocol
import Foundation
import UIKit
import UserNotifications

/// "Ring my phone" from the PC (`RingDevice`). In the foreground Fenpo plays a loud loop that
/// ignores the silent switch; in the background iOS only allows a notification sound, which the
/// silent switch mutes (PLAN: "Sessiz modda kısıtlı").
@MainActor
public final class PhoneRinger {
    /// Bundle resource played for the ring.
    public static let soundName = "fenpo-ring.wav"

    private static let notificationID = "com.fenpo.ring"

    /// The phone is ringing in the foreground.
    public private(set) var isRinging = false {
        didSet {
            if isRinging != oldValue {
                onChange?(isRinging)
            }
        }
    }

    /// Called when `isRinging` changes.
    public var onChange: ((Bool) -> Void)?

    private var player: AVAudioPlayer?
    private let log = FenpoLog("Ring")

    /// Creates the ringer.
    public init() {
    }

    /// Starts or stops ringing as the PC asks.
    /// - Parameter request: Message from the PC.
    public func handle(_ request: Fenpo_V1_RingDevice) {
        if request.stop {
            stop()
        } else {
            start()
        }
    }

    /// Rings until `stop()`, the user stops it or the sound ends.
    public func start() {
        let state = UIApplication.shared.applicationState
        log.info("Ring requested, app \(state.logName)")
        if state == .active {
            playLoop()
        } else {
            postNotification()
        }
    }

    /// Stops the sound and removes the ring notification.
    public func stop() {
        player?.stop()
        player = nil
        isRinging = false
        let center = UNUserNotificationCenter.current()
        center.removeDeliveredNotifications(withIdentifiers: [Self.notificationID])
        center.removePendingNotificationRequests(withIdentifiers: [Self.notificationID])
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
        log.info("Ring stopped")
    }

    /// Asks for notification permission once; ringing in the background needs it.
    public func requestPermissionIfNeeded() async {
        let center = UNUserNotificationCenter.current()
        let settings = await center.notificationSettings()
        guard settings.authorizationStatus == .notDetermined else {
            return
        }
        let granted = (try? await center.requestAuthorization(options: [.alert, .sound])) ?? false
        log.info("Notification permission \(granted ? "granted" : "denied")")
    }

    private func playLoop() {
        guard let url = Bundle.main.url(forResource: Self.soundName, withExtension: nil) else {
            log.error("Ring sound missing from the bundle")
            return
        }
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .default)
            try session.setActive(true)
            let player = try AVAudioPlayer(contentsOf: url)
            player.numberOfLoops = 3
            player.volume = 1
            player.play()
            self.player = player
            isRinging = true
            let duration = player.duration * Double(player.numberOfLoops + 1)
            Task { [weak self] in
                try? await Task.sleep(for: .seconds(duration))
                if let self, self.player === player {
                    self.stop()
                }
            }
        } catch {
            log.error("Playing the ring failed: \(String(describing: error))")
        }
    }

    private func postNotification() {
        let content = UNMutableNotificationContent()
        content.title = String(localized: "ring.notification.title")
        content.body = String(localized: "ring.notification.body")
        content.sound = UNNotificationSound(named: UNNotificationSoundName(Self.soundName))
        content.interruptionLevel = .active
        let request = UNNotificationRequest(identifier: Self.notificationID, content: content, trigger: nil)
        let log = log
        UNUserNotificationCenter.current().add(request) { error in
            if let error {
                log.error("Ring notification failed: \(String(describing: error))")
            }
        }
    }
}
#endif

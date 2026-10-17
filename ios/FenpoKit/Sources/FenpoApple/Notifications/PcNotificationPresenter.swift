#if os(iOS)
import CryptoKit
import FenpoCore
import FenpoProtocol
import Foundation
import UserNotifications

/// The PC's notifications on the iPhone (protocol/README.md, "PC notifications on the phone"): one local
/// notification per `PcNotification`, grouped by app, removed again with `PcNotificationRemoved`. Icons
/// come only over Wi-Fi and are cached per app, so later ones over Bluetooth show them too.
@MainActor
public final class PcNotificationPresenter {
    /// The Settings switch (App Group defaults); on unless the user turned it off.
    public static let enabledKey = "feature.pcNotifications"

    private let center = UNUserNotificationCenter.current()
    private let icons: URL
    private let log = FenpoLog("PcNotifications")

    /// Creates the presenter.
    public init() {
        icons = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("pc-app-icons", isDirectory: true)
    }

    /// Whether the user wants the PC's notifications on the iPhone.
    public var isEnabled: Bool {
        FeatureAccess.isAvailable(.pcNotifications)
            && (UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.object(forKey: Self.enabledKey) as? Bool ?? true)
    }

    /// Shows a notification from the PC.
    /// - Parameter notification: What the PC sent.
    public func show(_ notification: Fenpo_V1_PcNotification) {
        guard isEnabled else {
            return
        }
        if !notification.appIconPng.isEmpty {
            cacheIcon(notification.appIconPng, for: notification.appID)
        }
        let content = UNMutableNotificationContent()
        if notification.title.isEmpty {
            content.title = notification.appName
        } else {
            content.title = notification.title
            content.subtitle = notification.appName
        }
        content.body = notification.text
        content.threadIdentifier = "pc.\(notification.appID)"
        content.sound = .default
        content.interruptionLevel = .active
        if let attachment = iconAttachment(for: notification.appID) {
            content.attachments = [attachment]
        }
        let request = UNNotificationRequest(identifier: Self.identifier(notification.id), content: content, trigger: nil)
        center.add(request) { [log] error in
            if let error {
                log.error("PC notification not shown: \(String(describing: error))")
            }
        }
        log.info("PC notification from \(notification.appID)")
    }

    /// Removes a notification the PC no longer has.
    /// - Parameter id: Its id on the PC.
    public func remove(_ id: String) {
        let identifier = Self.identifier(id)
        center.removeDeliveredNotifications(withIdentifiers: [identifier])
        center.removePendingNotificationRequests(withIdentifiers: [identifier])
    }

    private static func identifier(_ id: String) -> String {
        "com.fenpo.pc-notification.\(id)"
    }

    private func iconURL(for appID: String) -> URL {
        let name = SHA256.hash(data: Data(appID.utf8)).prefix(12).map { String(format: "%02x", $0) }.joined()
        return icons.appendingPathComponent("\(name).png")
    }

    private func cacheIcon(_ png: Data, for appID: String) {
        do {
            try FileManager.default.createDirectory(at: icons, withIntermediateDirectories: true)
            try png.write(to: iconURL(for: appID), options: .atomic)
        } catch {
            log.error("Icon not cached: \(String(describing: error))")
        }
    }

    /// The system moves an attachment's file into its own store, so it gets a copy.
    private func iconAttachment(for appID: String) -> UNNotificationAttachment? {
        let cached = iconURL(for: appID)
        guard FileManager.default.fileExists(atPath: cached.path) else {
            return nil
        }
        let copy = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).png")
        do {
            try FileManager.default.copyItem(at: cached, to: copy)
            return try UNNotificationAttachment(identifier: "icon", url: copy)
        } catch {
            return nil
        }
    }
}
#endif

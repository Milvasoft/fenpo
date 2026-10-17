#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UserNotifications

/// The iPhone's Personal Hotspot for the PC (protocol/README.md, "Hotspot"). iOS lets no app read the
/// hotspot's name and password or turn it on, so the user types them into Fenpo once; they stay in the
/// keychain and go to the PC after every `Hello`. A `HotspotRequest` shows where to turn it on.
@MainActor
public final class HotspotService {
    /// Saved hotspot details.
    public struct Details: Codable, Equatable, Sendable {
        public var ssid: String
        public var passphrase: String

        /// Creates details.
        public init(ssid: String, passphrase: String) {
            self.ssid = ssid
            self.passphrase = passphrase
        }
    }

    /// Sends a message to the PC on a channel (set by `FenpoServices`).
    public var send: ((Fenpo_V1_Envelope.OneOf_Body, FenpoServices.Channel) -> Void)?

    /// Channels that finished their handshake and can take `HotspotInfo` now.
    public var openChannels: () -> [FenpoServices.Channel] = { [] }

    private let keychain: Keychain
    private let log = FenpoLog("Hotspot")
    private static let service = "com.fenpo.hotspot"
    private static let account = "personal-hotspot"

    /// Creates the service.
    /// - Parameter keychain: Where the details are kept.
    public init(keychain: Keychain = .shared) {
        self.keychain = keychain
    }

    /// The saved details, if any.
    public var details: Details? {
        guard let data = try? keychain.data(service: Self.service, account: Self.account) else {
            return nil
        }
        return try? JSONDecoder().decode(Details.self, from: data)
    }

    /// Saves new details and sends them to the PC.
    /// - Parameter details: Name and password as the user typed them.
    public func save(_ details: Details) throws {
        try keychain.set(try JSONEncoder().encode(details), service: Self.service, account: Self.account)
        log.info("Hotspot details saved")
        sendInfoEverywhere()
    }

    /// Removes the details here and on the PC.
    public func clear() throws {
        try keychain.delete(service: Self.service, account: Self.account)
        log.info("Hotspot details removed")
        sendInfoEverywhere()
    }

    /// Sends the details after the PC's `Hello` on a channel.
    /// - Parameter channel: Channel that just finished its handshake.
    public func sendInfo(over channel: FenpoServices.Channel) {
        guard let info = info() else {
            return
        }
        send?(.hotspotInfo(info), channel)
    }

    /// Answers the PC's "Connect to hotspot".
    public func handleRequest() -> Fenpo_V1_HotspotStatus {
        var status = Fenpo_V1_HotspotStatus()
        guard FeatureAccess.isAvailable(.hotspot), details != nil else {
            status.state = .notSetUp
            log.info("Hotspot requested, not set up")
            return status
        }
        let content = UNMutableNotificationContent()
        content.title = String(localized: "hotspot.notification.title")
        content.body = String(localized: "hotspot.notification.body")
        content.sound = .default
        content.interruptionLevel = .timeSensitive
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "com.fenpo.hotspot", content: content, trigger: nil))
        status.state = .askedUser
        log.info("Hotspot requested, asked the user")
        return status
    }

    /// What `HotspotInfo` carries now; `cleared` without saved details. `nil` only when nothing was
    /// ever saved, so a PC is not told to forget what it never had.
    private func info() -> Fenpo_V1_HotspotInfo? {
        var info = Fenpo_V1_HotspotInfo()
        if let details {
            info.ssid = details.ssid
            info.passphrase = details.passphrase
            UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.set(true, forKey: "hotspot.everSaved")
            return info
        }
        guard UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.bool(forKey: "hotspot.everSaved") == true else {
            return nil
        }
        info.cleared = true
        return info
    }

    private func sendInfoEverywhere() {
        for channel in openChannels() {
            sendInfo(over: channel)
        }
    }
}
#endif

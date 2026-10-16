#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import LocalAuthentication
import Security
import UIKit
import UserNotifications

/// Unlocking the PC's locked session with Face ID (protocol/README.md, "Unlocking the PC"). The PC keeps
/// the Windows password encrypted under a 32-byte key that only this iPhone keeps, in the keychain
/// behind the current Face ID enrolment. The key leaves the phone only after Face ID, and only on the
/// sealed BLE channel or the TLS channel. Face ID needs Fenpo in front, so a request that arrives in the
/// background becomes a notification and waits for a minute.
@MainActor
public final class UnlockService {
    /// What waits for the user's Face ID.
    public struct Prompt: Identifiable, Equatable, Sendable {
        public enum Kind: Equatable, Sendable {
            /// The PC sets up unlocking.
            case enroll
            /// The PC wants to unlock; the answer echoes this id.
            case unlock(requestID: String)
        }

        public let id = UUID()
        public let pcName: String
        public let kind: Kind
        let channel: FenpoServices.Channel
        let arrived: Date
    }

    /// The prompt Fenpo should show now; set while Fenpo is in front.
    public private(set) var prompt: Prompt? {
        didSet {
            onChange?()
        }
    }

    /// Called when `prompt` or the key changes.
    public var onChange: (() -> Void)?

    /// Sends a message to the PC; the reply id is 0 for answers that come later.
    public var send: ((Fenpo_V1_Envelope.OneOf_Body, UInt64, FenpoServices.Channel) -> Void)?

    private let store: PairedPCStore
    private var waiting: Prompt?
    private var requestEnvelopeID: UInt64 = 0
    private let log = FenpoLog("Unlock")
    private nonisolated static let service = "com.fenpo.unlock"
    /// The PC gives up after a minute.
    private static let lifetime: TimeInterval = 60

    /// Creates the service.
    /// - Parameter store: The paired PC, whose id names the key.
    public init(store: PairedPCStore) {
        self.store = store
    }

    /// Whether a key for the paired PC exists (without asking for Face ID).
    public var isSetUp: Bool {
        guard let account = account else {
            return false
        }
        let context = LAContext()
        context.interactionNotAllowed = true
        var query = Self.query(account: account)
        query[kSecUseAuthenticationContext as String] = context
        query[kSecReturnData as String] = false
        let status = SecItemCopyMatching(query as CFDictionary, nil)
        return status == errSecSuccess || status == errSecInteractionNotAllowed
    }

    /// Handles unlock messages; returns whether it took the message.
    /// - Parameters:
    ///   - envelope: Message from the PC.
    ///   - channel: Channel it came on.
    public func handle(_ envelope: Fenpo_V1_Envelope, from channel: FenpoServices.Channel) -> Bool {
        switch envelope.body {
        case .unlockEnroll(let enroll):
            log.info("PC sets up unlocking")
            ask(Prompt(pcName: enroll.pcName, kind: .enroll, channel: channel, arrived: .now), envelopeID: envelope.id)
            return true
        case .unlockRequest(let request):
            guard isSetUp else {
                log.info("Unlock asked without a key")
                reply(error: .notPaired, to: envelope.id, on: channel)
                return true
            }
            log.info("PC asks to unlock")
            ask(Prompt(pcName: request.pcName, kind: .unlock(requestID: request.requestID), channel: channel, arrived: .now), envelopeID: envelope.id)
            return true
        case .unlockRevoke:
            removeKey()
            return true
        default:
            return false
        }
    }

    /// Fenpo came to the front: show what waited, if the PC still waits.
    public func appBecameActive() {
        guard let waiting, Date().timeIntervalSince(waiting.arrived) < Self.lifetime else {
            waiting = nil
            return
        }
        self.waiting = nil
        prompt = waiting
    }

    /// The user approved: Face ID, then the key goes to the PC.
    public func approve() async {
        guard let prompt else {
            return
        }
        self.prompt = nil
        guard let account else {
            reply(error: .notPaired, to: requestEnvelopeID, on: prompt.channel)
            return
        }
        let reason = String(localized: "unlock.reason \(prompt.pcName)")
        do {
            if prompt.kind == .enroll {
                try Self.createKey(account: account)
            }
            let key = try await Self.readKey(account: account, reason: reason)
            var answer = Fenpo_V1_UnlockKey()
            if case .unlock(let requestID) = prompt.kind {
                answer.requestID = requestID
            }
            answer.key = key
            send?(.unlockKey(answer), requestEnvelopeID, prompt.channel)
            log.info(prompt.kind == .enroll ? "Unlocking set up" : "Unlock key sent")
            onChange?()
        } catch UnlockError.noKey {
            reply(error: .notPaired, to: requestEnvelopeID, on: prompt.channel)
        } catch {
            log.info("Face ID not passed: \(String(describing: error))")
            reply(error: .declined, to: requestEnvelopeID, on: prompt.channel)
        }
    }

    /// The user said no.
    public func decline() {
        guard let prompt else {
            return
        }
        self.prompt = nil
        reply(error: .declined, to: requestEnvelopeID, on: prompt.channel)
    }

    /// Forgets the key (Settings, or `UnlockRevoke`); the PC then asks for the password again.
    public func removeKey() {
        guard let account else {
            return
        }
        SecItemDelete(Self.query(account: account) as CFDictionary)
        log.info("Unlock key removed")
        onChange?()
    }

    private var account: String? {
        store.load()?.pcID
    }

    private func ask(_ prompt: Prompt, envelopeID: UInt64) {
        requestEnvelopeID = envelopeID
        if UIApplication.shared.applicationState == .active {
            self.prompt = prompt
            return
        }
        waiting = prompt
        let content = UNMutableNotificationContent()
        switch prompt.kind {
        case .enroll:
            content.title = String(localized: "unlock.notification.enroll.title")
        case .unlock:
            content.title = String(localized: "unlock.notification.title \(prompt.pcName)")
        }
        content.body = String(localized: "unlock.notification.body")
        content.interruptionLevel = .timeSensitive
        content.sound = .default
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "com.fenpo.unlock", content: content, trigger: nil))
    }

    private func reply(error code: Fenpo_V1_ErrorCode, to envelopeID: UInt64, on channel: FenpoServices.Channel) {
        var error = Fenpo_V1_Error()
        error.code = code
        send?(.error(error), envelopeID, channel)
    }

    // MARK: - Keychain

    private enum UnlockError: Error {
        case noKey
        case keychain(OSStatus)
    }

    private nonisolated static func query(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }

    /// A new random key behind the current Face ID enrolment, replacing an older one.
    private nonisolated static func createKey(account: String) throws {
        var bytes = [UInt8](repeating: 0, count: 32)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else {
            throw UnlockError.keychain(errSecAllocate)
        }
        var error: Unmanaged<CFError>?
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly, .biometryCurrentSet, &error) else {
            throw UnlockError.keychain(errSecParam)
        }
        SecItemDelete(query(account: account) as CFDictionary)
        var item = query(account: account)
        item[kSecValueData as String] = Data(bytes)
        item[kSecAttrAccessControl as String] = access
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw UnlockError.keychain(status)
        }
    }

    /// Reads the key; iOS shows Face ID. Runs off the main thread, since the call waits for the user.
    private nonisolated static func readKey(account: String, reason: String) async throws -> Data {
        try await Task.detached {
            let context = LAContext()
            context.localizedReason = reason
            var query = query(account: account)
            query[kSecReturnData as String] = true
            query[kSecMatchLimit as String] = kSecMatchLimitOne
            query[kSecUseAuthenticationContext as String] = context
            var result: CFTypeRef?
            let status = SecItemCopyMatching(query as CFDictionary, &result)
            switch status {
            case errSecSuccess:
                guard let data = result as? Data, data.count == 32 else {
                    throw UnlockError.noKey
                }
                return data
            case errSecItemNotFound:
                throw UnlockError.noKey
            default:
                throw UnlockError.keychain(status)
            }
        }.value
    }
}
#endif

import Foundation
import Security

/// A failed keychain call.
public struct KeychainError: Error, Equatable, Sendable {
    /// The `OSStatus` the keychain returned.
    public let status: OSStatus

    /// Creates an error from a status.
    /// - Parameter status: Keychain status.
    public init(status: OSStatus) {
        self.status = status
    }
}

/// Generic password items in the keychain group shared by the app and its extensions.
public struct Keychain: Sendable {
    /// Shared access group with the team prefix, or `nil` for the default group (tests).
    public let accessGroup: String?

    /// The group from the `FenpoKeychainGroup` Info.plist key (`$(AppIdentifierPrefix)com.fenpo.shared`).
    public static let shared = Keychain(accessGroup: Bundle.main.object(forInfoDictionaryKey: "FenpoKeychainGroup") as? String)

    /// Creates a keychain wrapper.
    /// - Parameter accessGroup: Access group, or `nil` for the default.
    public init(accessGroup: String?) {
        self.accessGroup = accessGroup
    }

    /// Reads an item.
    /// - Parameters:
    ///   - service: Item service.
    ///   - account: Item account.
    public func data(service: String, account: String) throws -> Data? {
        var query = baseQuery(service: service, account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            return result as? Data
        case errSecItemNotFound:
            return nil
        default:
            throw KeychainError(status: status)
        }
    }

    /// Writes an item, replacing an existing one.
    /// - Parameters:
    ///   - data: Item contents.
    ///   - service: Item service.
    ///   - account: Item account.
    public func set(_ data: Data, service: String, account: String) throws {
        let query = baseQuery(service: service, account: account)
        let update = [kSecValueData as String: data]
        var status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var item = query
            item[kSecValueData as String] = data
            item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            status = SecItemAdd(item as CFDictionary, nil)
        }
        guard status == errSecSuccess else {
            throw KeychainError(status: status)
        }
    }

    /// Deletes an item; succeeds when it does not exist.
    /// - Parameters:
    ///   - service: Item service.
    ///   - account: Item account.
    public func delete(service: String, account: String) throws {
        let status = SecItemDelete(baseQuery(service: service, account: account) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw KeychainError(status: status)
        }
    }

    /// Adds the access group to a query when one is set.
    /// - Parameter query: Query to extend.
    func scoped(_ query: [String: Any]) -> [String: Any] {
        var query = query
        if let accessGroup {
            query[kSecAttrAccessGroup as String] = accessGroup
        }
        return query
    }

    private func baseQuery(service: String, account: String) -> [String: Any] {
        scoped([
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ])
    }
}

#if os(iOS)
import FenpoCore
import Foundation
import Security

/// When and with which version Fenpo was first opened on this iPhone. If features become paid later,
/// StoreKit's `AppTransaction.originalAppVersion` and `originalPurchaseDate` tell who downloaded it
/// while it was free; this local record is a second source and works without the App Store.
///
/// On the first launch it also removes what a previous install left in the keychain: iOS keeps
/// keychain items after an app is deleted, while the App Group container (the pairing record, this
/// record) goes with it.
public enum InstallRecord {
    private static let versionKey = "install.firstVersion"
    private static let dateKey = "install.firstDate"
    private static let leftoverServices = ["com.fenpo.ble-key", "com.fenpo.ble-counters", "com.fenpo.hotspot"]

    /// Version of the first launch, e.g. "0.1.0".
    public static var firstVersion: String? {
        UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.string(forKey: versionKey)
    }

    /// Date of the first launch.
    public static var firstDate: Date? {
        UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.object(forKey: dateKey) as? Date
    }

    /// Records the first launch once; call at launch.
    /// - Parameters:
    ///   - pairedStore: A pairing record means this is an update, not a new install: nothing is removed.
    ///   - identityStore: The phone's identity, removed with the other leftovers.
    public static func recordFirstLaunch(pairedStore: PairedPCStore, identityStore: DeviceIdentityStore) {
        guard let defaults = UserDefaults(suiteName: FenpoIdentifiers.appGroup), defaults.object(forKey: dateKey) == nil else {
            return
        }
        let log = FenpoLog("App")
        if pairedStore.load() == nil {
            removeKeychainLeftovers()
            try? identityStore.delete()
            log.info("First launch: keychain leftovers removed")
        }
        defaults.set(LocalDevice.appVersion, forKey: versionKey)
        defaults.set(Date(), forKey: dateKey)
        log.info("First launch recorded, version \(LocalDevice.appVersion)")
    }

    private static func removeKeychainLeftovers() {
        for service in leftoverServices {
            var query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: service,
            ]
            if let group = Keychain.shared.accessGroup {
                query[kSecAttrAccessGroup as String] = group
            }
            SecItemDelete(query as CFDictionary)
        }
    }
}
#endif

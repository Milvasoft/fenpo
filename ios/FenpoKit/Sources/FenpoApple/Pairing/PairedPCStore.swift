import FenpoCore
import Foundation

/// What the phone remembers about the paired PC.
public struct PairedPCRecord: Codable, Equatable, Sendable {
    /// Stable PC id.
    public var pcID: String

    /// User visible name.
    public var name: String

    /// SHA-256 of the PC certificate, pinned on every connection.
    public var certificateSHA256: [UInt8]

    /// Last known data channel addresses, as `host:port`.
    public var endpoints: [String]

    /// Bluetooth identifier of the PC as AccessorySetupKit reported it.
    public var bluetoothIdentifier: UUID?

    /// When pairing finished.
    public var pairedAt: Date

    /// Creates a record.
    public init(pcID: String, name: String, certificateSHA256: [UInt8], endpoints: [String], bluetoothIdentifier: UUID? = nil, pairedAt: Date = .now) {
        self.pcID = pcID
        self.name = name
        self.certificateSHA256 = certificateSHA256
        self.endpoints = endpoints
        self.bluetoothIdentifier = bluetoothIdentifier
        self.pairedAt = pairedAt
    }
}

/// Keeps the pairing record in the App Group and the BLE channel key and counters in the keychain.
public struct PairedPCStore: Sendable {
    private static let recordKey = "pairing.record"
    private static let keyService = "com.fenpo.ble-key"
    private static let countersService = "com.fenpo.ble-counters"

    private let suiteName: String?
    private let keychain: Keychain

    /// Creates a store.
    /// - Parameters:
    ///   - suiteName: UserDefaults suite; the App Group by default.
    ///   - keychain: Keychain group for secrets.
    public init(suiteName: String? = FenpoIdentifiers.appGroup, keychain: Keychain = .shared) {
        self.suiteName = suiteName
        self.keychain = keychain
    }

    private var defaults: UserDefaults {
        suiteName.flatMap(UserDefaults.init(suiteName:)) ?? .standard
    }

    /// The paired PC, if any.
    public func load() -> PairedPCRecord? {
        guard let data = defaults.data(forKey: Self.recordKey) else {
            return nil
        }
        return try? JSONDecoder().decode(PairedPCRecord.self, from: data)
    }

    /// Saves a new pairing with its BLE channel key, replacing any previous one.
    /// - Parameters:
    ///   - record: Pairing record.
    ///   - bleChannelKey: `PairingResult.ble_channel_key`.
    public func save(_ record: PairedPCRecord, bleChannelKey: [UInt8]) throws {
        if let previous = load(), previous.pcID != record.pcID {
            try removeSecrets(for: previous.pcID)
        }
        try keychain.set(Data(bleChannelKey), service: Self.keyService, account: record.pcID)
        try saveCounters(BLEChannelCounters(), for: record.pcID)
        try update(record)
    }

    /// Updates the record without touching the secrets.
    /// - Parameter record: New record.
    public func update(_ record: PairedPCRecord) throws {
        defaults.set(try JSONEncoder().encode(record), forKey: Self.recordKey)
    }

    /// The BLE channel key of a PC.
    /// - Parameter pcID: PC id.
    public func bleChannelKey(for pcID: String) throws -> [UInt8]? {
        try keychain.data(service: Self.keyService, account: pcID).map(Array.init)
    }

    /// Persisted AES-GCM counters of a PC's channel key.
    /// - Parameter pcID: PC id.
    public func counters(for pcID: String) throws -> BLEChannelCounters {
        guard let data = try keychain.data(service: Self.countersService, account: pcID) else {
            return BLEChannelCounters()
        }
        return try JSONDecoder().decode(BLEChannelCounters.self, from: data)
    }

    /// Saves AES-GCM counters; call after every seal and open.
    /// - Parameters:
    ///   - counters: Current counters.
    ///   - pcID: PC id.
    public func saveCounters(_ counters: BLEChannelCounters, for pcID: String) throws {
        try keychain.set(try JSONEncoder().encode(counters), service: Self.countersService, account: pcID)
    }

    /// Forgets the paired PC and its secrets.
    public func remove() throws {
        if let record = load() {
            try removeSecrets(for: record.pcID)
        }
        defaults.removeObject(forKey: Self.recordKey)
    }

    private func removeSecrets(for pcID: String) throws {
        try keychain.delete(service: Self.keyService, account: pcID)
        try keychain.delete(service: Self.countersService, account: pcID)
    }
}

/// Facts about this phone for `Hello` and `PairingRequest`.
public enum LocalDevice {
    private static let deviceIDKey = "device.id"

    /// Stable id created at first launch, shared with the extensions.
    public static var deviceID: String {
        let defaults = UserDefaults(suiteName: FenpoIdentifiers.appGroup) ?? .standard
        if let id = defaults.string(forKey: deviceIDKey) {
            return id
        }
        let id = UUID().uuidString
        defaults.set(id, forKey: deviceIDKey)
        return id
    }

    /// Operating system and version, e.g. "iOS 27.0.1", for `Hello.os_version`.
    public static var osVersion: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        let patch = version.patchVersion > 0 ? ".\(version.patchVersion)" : ""
        return "iOS \(version.majorVersion).\(version.minorVersion)\(patch)"
    }

    /// Hardware model identifier, e.g. "iPhone14,5", for `Hello.model`.
    public static var model: String {
        var info = utsname()
        uname(&info)
        return withUnsafeBytes(of: &info.machine) { bytes in
            String(decoding: bytes.prefix { $0 != 0 }, as: UTF8.self)
        }
    }

    /// Marketing version of the running app.
    public static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }
}

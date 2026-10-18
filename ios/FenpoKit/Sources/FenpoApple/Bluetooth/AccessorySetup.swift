#if os(iOS)
import AccessorySetupKit
@preconcurrency import CoreBluetooth
import FenpoCore
import UIKit

/// Adds the PC as an accessory through AccessorySetupKit, which lets iOS relaunch Fenpo after a
/// force quit or a Bluetooth toggle (RESEARCH §6). Create once at launch and call `activate()`.
@MainActor
public final class AccessorySetup {
    /// Why the picker did not return an accessory.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The user closed the picker.
        case cancelled

        /// The picked accessory has no Bluetooth identifier.
        case missingBluetoothIdentifier

        /// The session is not activated yet.
        case notActivated
    }

    private let session = ASAccessorySession()
    private var isActivated = false
    private var pickerContinuation: CheckedContinuation<ASAccessory, Swift.Error>?
    /// PC names whose name-filtered picker was closed without a PC: the next picker for them lists
    /// every Fenpo PC, in case the PC's Bluetooth name does not contain its pairing name.
    private var unfilteredNames: Set<String> = []
    private let log = FenpoLog("AccessorySetup")

    /// Accessories this app has added.
    public private(set) var accessories: [ASAccessory] = []

    /// Creates the setup helper.
    public init() {
    }

    /// Starts the session. Safe to call more than once.
    public func activate() {
        guard !isActivated else {
            return
        }
        isActivated = true
        session.activate(on: .main) { [weak self] event in
            MainActor.assumeIsolated {
                self?.handle(event)
            }
        }
    }

    /// Shows the system picker for nearby Fenpo PCs and returns the one the user added. Every Fenpo
    /// PC advertises the same service, so the picker only lists PCs whose Bluetooth name contains
    /// the pairing name (S4); if the user closes it without a PC, the next try lists them all and
    /// `BLECentral` turns away another PC by its name before bonding.
    /// - Parameters:
    ///   - name: PC name from pairing, shown in the picker.
    ///   - image: Picture shown in the picker.
    public func pickPC(named name: String, image: UIImage) async throws -> ASAccessory {
        guard isActivated else {
            throw Error.notActivated
        }
        let descriptor = ASDiscoveryDescriptor()
        descriptor.bluetoothServiceUUID = CBUUID(string: BLEChannelIdentifiers.service)
        let filtered = !unfilteredNames.contains(name)
        if filtered {
            descriptor.bluetoothNameSubstring = name
        }
        let item = ASPickerDisplayItem(name: name, productImage: image, descriptor: descriptor)
        log.info("Showing the accessory picker\(filtered ? " for the PC's name" : " for every Fenpo PC")")
        do {
            return try await showPicker(for: item)
        } catch Error.cancelled where filtered {
            unfilteredNames.insert(name)
            throw Error.cancelled
        }
    }

    private func showPicker(for item: ASPickerDisplayItem) async throws -> ASAccessory {
        try await withCheckedThrowingContinuation { continuation in
            pickerContinuation = continuation
            session.showPicker(for: [item]) { [weak self] error in
                guard let error else {
                    return
                }
                MainActor.assumeIsolated {
                    self?.finishPicker(throwing: error)
                }
            }
        }
    }

    /// Removes an accessory, e.g. when unpairing.
    /// - Parameter bluetoothIdentifier: Identifier of the accessory to remove.
    public func remove(bluetoothIdentifier: UUID) async throws {
        guard let accessory = accessories.first(where: { $0.bluetoothIdentifier == bluetoothIdentifier }) else {
            return
        }
        try await session.removeAccessory(accessory)
    }

    private func handle(_ event: ASAccessoryEvent) {
        switch event.eventType {
        case .activated:
            accessories = session.accessories
            log.info("Accessory session active with \(self.accessories.count) accessory(ies)")
        case .accessoryAdded:
            accessories = session.accessories
            if let accessory = event.accessory, let continuation = pickerContinuation {
                pickerContinuation = nil
                continuation.resume(returning: accessory)
            }
        case .accessoryRemoved, .accessoryChanged:
            accessories = session.accessories
        case .pickerDidDismiss:
            finishPicker(throwing: Error.cancelled)
        default:
            break
        }
    }

    private func finishPicker(throwing error: Swift.Error) {
        guard let continuation = pickerContinuation else {
            return
        }
        pickerContinuation = nil
        continuation.resume(throwing: error)
    }
}
#endif

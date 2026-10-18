import Foundation

/// Tells whether a Bluetooth peripheral's name belongs to the paired PC, so the phone never bonds
/// with another Fenpo PC nearby (two PCs advertise the same Fenpo service).
///
/// The name from pairing is Windows' NetBIOS computer name: upper case and at most 15 characters.
/// The PC's Bluetooth name is the full device name with its own capitalisation. The two match
/// when one starts with the other, ignoring case.
public enum PCBluetoothName {
    /// Whether the names can be the same PC; `true` when either is empty (nothing to compare).
    /// - Parameters:
    ///   - bluetoothName: Name the peripheral reports over Bluetooth.
    ///   - pairedName: PC name from pairing.
    public static func matches(bluetoothName: String, pairedName: String) -> Bool {
        let bluetooth = normalized(bluetoothName)
        let paired = normalized(pairedName)
        guard !bluetooth.isEmpty, !paired.isEmpty else {
            return true
        }
        return bluetooth.hasPrefix(paired) || paired.hasPrefix(bluetooth)
    }

    private static func normalized(_ name: String) -> String {
        name.trimmingCharacters(in: .whitespacesAndNewlines).uppercased(with: Locale(identifier: "en_US_POSIX"))
    }
}

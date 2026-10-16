#if os(iOS)
import FileProvider
import Foundation

/// The Files app location of the paired PC's shared folders: added under the PC's name while a PC
/// is paired, removed after unpairing.
public enum PCFilesDomain {
    private static let identifier = NSFileProviderDomainIdentifier(rawValue: "pc")

    /// Makes the Files app match the pairing.
    /// - Parameter pcName: Paired PC's name, or `nil` without a pairing.
    public static func sync(pcName: String?) async {
        let log = FenpoLog("Files")
        do {
            if let pcName {
                try await NSFileProviderManager.add(NSFileProviderDomain(identifier: identifier, displayName: pcName))
                log.info("Files location added")
            } else {
                try await NSFileProviderManager.removeAllDomains()
                log.info("Files location removed")
            }
        } catch {
            log.error("Files location not updated: \(String(describing: error))")
        }
    }
}
#endif

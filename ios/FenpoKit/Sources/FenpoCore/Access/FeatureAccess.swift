/// A feature that could one day be part of a paid tier.
public enum Feature: String, CaseIterable, Sendable {
    case clipboard
    case fileTransfer
    case filesApp
    case photoLibrary
    case screenshots
    case camera
    case screenMirror
    case remoteControl
    case pcNotifications
    case pcMedia
    case hotspot
    case focusSync
}

/// The one place that says whether a feature may be used. Everything is free today. If Buğra makes
/// some features paid later, this is where StoreKit's entitlements and the "downloaded while it was
/// free" check (`AppTransaction.originalAppVersion`, `InstallRecord`) go; every entry point already
/// asks here, so gating is one change.
public enum FeatureAccess {
    /// Whether the feature may be used now.
    /// - Parameter feature: The feature.
    public static func isAvailable(_ feature: Feature) -> Bool {
        true
    }
}

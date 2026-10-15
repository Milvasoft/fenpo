import FenpoCore
import Foundation
import Observation
import SwiftUI

/// Keeps the appearance preference in the App Group so the app and the share extension follow the same choice.
@MainActor
@Observable
public final class AppearanceStore {
    private static let key = "appearance"

    private let defaults: UserDefaults

    /// The current preference. Setting it saves it immediately.
    public var preference: AppearancePreference {
        didSet {
            defaults.set(preference.rawValue, forKey: Self.key)
        }
    }

    /// Creates a store backed by the given defaults.
    /// - Parameter defaults: Storage; the App Group suite by default.
    public init(defaults: UserDefaults? = nil) {
        let store = defaults ?? UserDefaults(suiteName: FenpoIdentifiers.appGroup) ?? .standard
        self.defaults = store
        self.preference = AppearancePreference(storedValue: store.string(forKey: Self.key))
    }
}

extension AppearancePreference {
    /// The color scheme to force, or `nil` to follow the system.
    public var colorScheme: ColorScheme? {
        switch self {
        case .system:
            nil
        case .light:
            .light
        case .dark:
            .dark
        }
    }
}

#if canImport(UIKit)
import UIKit

extension AppearancePreference {
    /// The interface style to force on UIKit hosts such as the share extension.
    public var userInterfaceStyle: UIUserInterfaceStyle {
        switch self {
        case .system:
            .unspecified
        case .light:
            .light
        case .dark:
            .dark
        }
    }
}
#endif

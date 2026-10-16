import FenpoApple
import FenpoCore
import SwiftUI
import UIKit

/// Settings: appearance and language, features, and the permissions Fenpo depends on.
struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(AppearanceStore.self) private var appearance
    @Environment(\.openURL) private var openURL

    @AppStorage("feature.clipboard", store: UserDefaults(suiteName: FenpoIdentifiers.appGroup))
    private var clipboardSharing = true

    @AppStorage(FocusSync.enabledKey, store: UserDefaults(suiteName: FenpoIdentifiers.appGroup))
    private var focusSync = true

    @AppStorage(PcNotificationPresenter.enabledKey, store: UserDefaults(suiteName: FenpoIdentifiers.appGroup))
    private var pcNotifications = true

    @AppStorage(ScreenshotSender.enabledKey, store: UserDefaults(suiteName: FenpoIdentifiers.appGroup))
    private var sendsScreenshots = false

    @AppStorage("developer.unlocked")
    private var isDeveloperUnlocked = false
    @State private var isDeveloperPresented = false
    @State private var versionTaps = 0

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 0) {
                    CardRows {
                        NavigationLink {
                            AppearancePickerView()
                        } label: {
                            SettingsRowLayout(icon: .sun, color: .dusk, title: "settings.appearance") {
                                Text(appearance.preference.title)
                                    .foregroundStyle(.textSecondary)
                                chevron
                            }
                            .contentShape(.rect)
                        }
                        .buttonStyle(.plain)

                        DisclosureRow(icon: .globe, color: .blue, title: "settings.language", value: Text(verbatim: currentLanguageName)) {
                            openAppSettings()
                        }
                    }
                    .padding(.top, 8)

                    SectionCaption(title: "settings.section.features")
                    CardRows {
                        ToggleRow(icon: .clipboard, color: .bloom, title: "settings.clipboardSharing", isOn: $clipboardSharing)
                        ValueRow(icon: .hand, color: .ink, title: "settings.backTap", value: Text("home.sendClipboard"))
                        ToggleRow(icon: .moon, color: .orange, title: "settings.focus", isOn: $focusSync)
                            .onChange(of: focusSync) {
                                Task { await model.focusSyncChanged() }
                            }
                        ToggleRow(icon: .bell, color: .dusk, title: "settings.pcNotifications", isOn: $pcNotifications)
                        NavigationLink {
                            HotspotSettingsView()
                        } label: {
                            SettingsRowLayout(icon: .symbol("personalhotspot"), color: .green, title: "settings.hotspot") {
                                Text(model.hotspot?.details == nil ? "settings.hotspot.notSet" : "settings.hotspot.set")
                                    .foregroundStyle(.textSecondary)
                                chevron
                            }
                            .contentShape(.rect)
                        }
                        .buttonStyle(.plain)
                        ToggleRow(icon: .photo, color: .dusk, title: "settings.screenshots", isOn: $sendsScreenshots)
                            .onChange(of: sendsScreenshots) { _, isOn in
                                model.screenshots?.setEnabled(isOn)
                            }
                        ToggleRow(icon: .lock, color: .leaf, title: "settings.faceIDUnlock", isOn: Binding(
                            get: { model.isUnlockSetUp },
                            set: { isOn in
                                if !isOn {
                                    model.removeUnlock()
                                }
                            }
                        ))
                        .disabled(!model.isUnlockSetUp)
                    }
                    Text("settings.focus.hint")
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                        .padding(.horizontal, 16)
                        .padding(.top, 6)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Text("settings.faceIDUnlock.hint")
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                        .padding(.horizontal, 16)
                        .padding(.top, 6)
                        .frame(maxWidth: .infinity, alignment: .leading)

                    SectionCaption(title: "settings.section.permissions")
                    CardRows {
                        permissionRow(icon: .bluetooth, color: .blue, title: "settings.permission.bluetooth", state: model.permissions.bluetooth)
                        permissionRow(icon: .wifi, color: .green, title: "settings.permission.localNetwork", state: model.permissions.localNetwork)
                        permissionRow(icon: .paste, color: .slate, title: "settings.permission.paste", state: model.permissions.paste)
                        if model.permissions.photos == .notDetermined {
                            DisclosureRow(icon: .photo, color: .dusk, title: "settings.permission.photos", value: Text(PermissionState.notDetermined.title), valueColor: .accent) {
                                Task { await model.requestPhotoAccess() }
                            }
                        } else {
                            permissionRow(icon: .photo, color: .dusk, title: "settings.permission.photos", state: model.permissions.photos)
                        }
                    }
                    Text("settings.permission.paste.note")
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                        .padding(.horizontal, 16)
                        .padding(.top, 6)
                        .frame(maxWidth: .infinity, alignment: .leading)

                    if isDeveloperUnlocked {
                        SectionCaption(title: "developer.title")
                        CardRows {
                            DisclosureRow(icon: .symbol("hammer"), color: .ink, title: "developer.title", value: Text(verbatim: "")) {
                                isDeveloperPresented = true
                            }
                        }
                    }

                    versionFooter
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 24)
            }
            .fenpoScreenBackground()
            .navigationTitle("tab.settings")
            .navigationDestination(isPresented: $isDeveloperPresented) {
                DeveloperView()
            }
            .onAppear(perform: openScreenshotScene)
        }
    }

    /// App version; seven taps open the hidden Developer menu.
    private var versionFooter: some View {
        Text(verbatim: "Fenpo \(LocalDevice.appVersion) (\(Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?"))")
            .font(.footnote)
            .foregroundStyle(.textTertiary)
            .frame(maxWidth: .infinity)
            .padding(.top, 28)
            .contentShape(.rect)
            .onTapGesture {
                versionTaps += 1
                if versionTaps >= 7 {
                    versionTaps = 0
                    isDeveloperUnlocked = true
                    isDeveloperPresented = true
                }
            }
    }

    private func openScreenshotScene() {
        #if DEBUG
        if ScreenshotScene.current == .developer {
            isDeveloperPresented = true
        }
        #endif
    }

    private var chevron: some View {
        Image(systemName: "chevron.right")
            .font(.footnote.weight(.semibold))
            .foregroundStyle(.textTertiary)
    }

    /// Name of the language the app runs in, in that language ("Türkçe", "English").
    private var currentLanguageName: String {
        let code = Bundle.main.preferredLocalizations.first ?? "en"
        let language = Locale(identifier: code)
        return language.localizedString(forLanguageCode: code)?.capitalized(with: language) ?? code
    }

    @ViewBuilder
    private func permissionRow(icon: FenpoIcon, color: Color, title: LocalizedStringKey, state: PermissionState) -> some View {
        let value = Text(state.title)
        if state == .allowed {
            ValueRow(icon: icon, color: color, title: title, value: value)
        } else {
            // Only a known problem is in the accent color; an unknown state just leads to Settings.
            DisclosureRow(icon: icon, color: color, title: title, value: value, valueColor: state == .unknown ? .textSecondary : .accent) {
                openAppSettings()
            }
        }
    }

    private func openAppSettings() {
        if let url = URL(string: UIApplication.openSettingsURLString) {
            openURL(url)
        }
    }
}

extension AppearancePreference {
    /// User visible name.
    var title: LocalizedStringKey {
        switch self {
        case .system:
            "appearance.system"
        case .light:
            "appearance.light"
        case .dark:
            "appearance.dark"
        }
    }
}

extension PermissionState {
    /// User visible state.
    var title: LocalizedStringKey {
        switch self {
        case .allowed:
            "permission.allowed"
        case .denied:
            "permission.denied"
        case .notDetermined:
            "permission.notDetermined"
        case .unknown:
            "permission.unknown"
        }
    }
}

#Preview("Light · Türkçe") {
    SettingsView().sampleModel().preview(.light, "tr")
}

#Preview("Dark · Türkçe") {
    SettingsView().sampleModel().preview(.dark, "tr")
}

#Preview("Light · English") {
    SettingsView().sampleModel().preview(.light, "en")
}

#Preview("Dark · English") {
    SettingsView().sampleModel().preview(.dark, "en")
}

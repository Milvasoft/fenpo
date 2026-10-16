import FenpoApple
import SwiftUI

/// "Kişisel Erişim Noktası": the hotspot's name and password, typed once, so the PC can join it with one
/// click. iOS lets no app read them, and they stay in the keychain.
struct HotspotSettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var ssid = ""
    @State private var passphrase = ""
    @State private var hasSaved = false
    @State private var failed = false

    var body: some View {
        ScrollView {
            VStack(spacing: 0) {
                Text("hotspot.explanation")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .padding(.horizontal, 16)
                    .padding(.top, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)

                SectionCaption(title: "hotspot.section.details")
                CardRows(separatorInset: 16) {
                    TextField("hotspot.ssid", text: $ssid)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .padding(.horizontal, 16)
                        .frame(minHeight: 50)
                    SecureField("hotspot.passphrase", text: $passphrase)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .padding(.horizontal, 16)
                        .frame(minHeight: 50)
                }
                Text("hotspot.whereToFind")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .padding(.horizontal, 16)
                    .padding(.top, 6)
                    .frame(maxWidth: .infinity, alignment: .leading)

                if failed {
                    Text("hotspot.error.save")
                        .font(.footnote)
                        .foregroundStyle(.red)
                        .padding(.horizontal, 16)
                        .padding(.top, 10)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }

                Button("hotspot.save") {
                    save()
                }
                .buttonStyle(PrimaryPillButtonStyle())
                .disabled(ssid.trimmingCharacters(in: .whitespaces).isEmpty || passphrase.count < 8)
                .padding(.top, 20)

                if hasSaved {
                    Button("hotspot.remove", role: .destructive) {
                        remove()
                    }
                    .font(.body.weight(.semibold))
                    .padding(.top, 16)
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .fenpoScreenBackground()
        .navigationTitle("settings.hotspot")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            if let details = model.hotspot?.details {
                ssid = details.ssid
                passphrase = details.passphrase
                hasSaved = true
            }
        }
    }

    private func save() {
        do {
            try model.hotspot?.save(HotspotService.Details(ssid: ssid.trimmingCharacters(in: .whitespaces), passphrase: passphrase))
            failed = false
            dismiss()
        } catch {
            failed = true
        }
    }

    private func remove() {
        do {
            try model.hotspot?.clear()
            dismiss()
        } catch {
            failed = true
        }
    }
}

#Preview("Light · Türkçe") {
    NavigationStack {
        HotspotSettingsView()
    }
    .sampleModel()
    .preview(.light, "tr")
}

#Preview("Dark · English") {
    NavigationStack {
        HotspotSettingsView()
    }
    .sampleModel()
    .preview(.dark, "en")
}

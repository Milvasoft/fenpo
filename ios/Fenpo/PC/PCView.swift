import FenpoCore
import SwiftUI

/// The paired PC: connection paths and actions on the PC.
struct PCView: View {
    @Environment(AppModel.self) private var model
    @State private var isConfirmingUnpair = false

    var body: some View {
        @Bindable var model = model

        NavigationStack(path: $model.pcPath) {
            Group {
                if let pc = model.snapshot.pc {
                    ScrollView {
                        VStack(spacing: 0) {
                            PCCard(pc: pc, isConnected: model.snapshot.isConnected, proximity: model.snapshot.proximity, links: model.snapshot.links)

                            SectionCaption(title: "pc.section.links")
                            CardRows {
                                StatusRow(icon: .bluetooth, color: .blue, title: "link.bluetooth", isUp: model.snapshot.links.bluetooth)
                                StatusRow(icon: .wifi, color: .green, title: "link.wifi", isUp: model.snapshot.links.wifi)
                                StatusRow(icon: .usb, color: .slate, title: "link.usb", isUp: model.snapshot.links.usb)
                            }

                            SectionCaption(title: "pc.section.remote")
                            CardRows {
                                NavigationLink(value: AppModel.PCScreen.touchpad) {
                                    LinkRow(icon: .symbol("rectangle.and.hand.point.up.left"), color: .dusk, title: "remote.touchpad")
                                }
                                .buttonStyle(.plain)
                                NavigationLink(value: AppModel.PCScreen.presenter) {
                                    LinkRow(icon: .symbol("play.rectangle"), color: .bloom, title: "remote.presenter")
                                }
                                .buttonStyle(.plain)
                                BroadcastPickerRow()
                            }
                            Text("remote.screen.hint")
                                .font(.footnote)
                                .foregroundStyle(.textSecondary)
                                .padding(.horizontal, 16)
                                .padding(.top, 6)
                                .frame(maxWidth: .infinity, alignment: .leading)

                            SectionCaption(title: "pc.section.actions")
                            CardRows {
                                ActionRow(icon: .lock, color: .ink, title: "pc.lock") {
                                    Task { await model.lockPC() }
                                }
                                .disabled(!model.snapshot.isConnected)
                                ActionRow(icon: .symbol("xmark"), color: .red, title: "pc.unpair", role: .destructive) {
                                    isConfirmingUnpair = true
                                }
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.bottom, 24)
                    }
                    .confirmationDialog("pc.unpair.confirm.title \(pc.name)", isPresented: $isConfirmingUnpair, titleVisibility: .visible) {
                        Button("pc.unpair", role: .destructive) {
                            Task { await model.unpair() }
                        }
                    } message: {
                        Text("pc.unpair.confirm.message")
                    }
                } else {
                    ContentUnavailableView {
                        Label("pc.empty.title", systemImage: "display")
                    } description: {
                        Text("home.addPC.message")
                    } actions: {
                        Button("home.addPC.button") {
                            model.isPairingPresented = true
                        }
                        .buttonStyle(PrimaryPillButtonStyle())
                        .padding(.horizontal, 40)
                    }
                    .foregroundStyle(.textPrimary)
                }
            }
            .fenpoScreenBackground()
            .tabHeader("tab.pc")
            .navigationDestination(for: AppModel.PCScreen.self) { screen in
                switch screen {
                case .touchpad:
                    TouchpadView()
                case .presenter:
                    PresenterView()
                }
            }
        }
    }
}

/// A row that opens another screen.
struct LinkRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey

    var body: some View {
        SettingsRowLayout(icon: icon, color: color, title: title) {
            Image(systemName: "chevron.right")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(.textTertiary)
        }
        .contentShape(.rect)
    }
}

/// A row with an icon, a title and "Connected" or "Not connected".
struct StatusRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    let isUp: Bool

    var body: some View {
        SettingsRowLayout(icon: icon, color: color, title: title) {
            Text(isUp ? "pc.status.connected" : "pc.status.disconnected")
                .foregroundStyle(.textSecondary)
        }
    }
}

/// A tappable row that runs an action.
struct ActionRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    var role: ButtonRole?
    let action: () -> Void

    var body: some View {
        Button(role: role, action: action) {
            SettingsRowLayout(icon: icon, color: color, title: title, titleColor: role == .destructive ? .red : .textPrimary) {
                EmptyView()
            }
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
    }
}

#Preview("Light · Türkçe") {
    PCView().sampleModel().preview(.light, "tr")
}

#Preview("Dark · English") {
    PCView().sampleModel().preview(.dark, "en")
}

#Preview("Dark · Türkçe, not paired") {
    PCView().sampleModel(paired: false).preview(.dark, "tr")
}

#Preview("Light · English, not paired") {
    PCView().sampleModel(paired: false).preview(.light, "en")
}

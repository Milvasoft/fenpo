import FenpoCore
import SwiftUI

/// First tab: the paired PC, its connection paths, four quick actions and the latest transfers.
struct HomeView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 0) {
                    if let pc = model.snapshot.pc {
                        PCCard(pc: pc, isConnected: model.snapshot.isConnected, proximity: model.snapshot.proximity, links: model.snapshot.links)
                        if model.snapshot.notificationsShared == false {
                            NoticeBanner(title: "home.notificationsOff.title", message: Text("home.notificationsOff.message \(pc.name)"))
                                .padding(.top, 12)
                        }
                        quickActions
                            .padding(.top, 12)
                        if !model.snapshot.transfers.isEmpty {
                            SectionCaption(title: "home.recentTransfers")
                            CardRows(separatorInset: 64) {
                                ForEach(model.snapshot.transfers.prefix(3)) { record in
                                    NavigationLink(value: TransferRoute(id: record.id)) {
                                        TransferRow(record: record)
                                            .contentShape(.rect)
                                    }
                                    .buttonStyle(.plain)
                                }
                            }
                        }
                    } else {
                        AddPCCard {
                            model.isPairingPresented = true
                        }
                    }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 24)
            }
            .fenpoScreenBackground()
            .navigationDestination(for: TransferRoute.self) { route in
                TransferDetailView(recordID: route.id)
            }
            .tabHeader("app.name") {
                GlassCircleButton(systemImage: "plus", label: "home.addPC.accessibility") {
                    model.isPairingPresented = true
                }
            }
            .refreshable {
                await model.refresh()
            }
        }
    }

    private var cameraSubtitle: Text {
        guard let state = model.cameraState else {
            return Text("home.camera.subtitle")
        }
        return state.consumer.isEmpty ? Text("camera.live") : Text("camera.live.consumer \(state.consumer)")
    }

    private var quickActions: some View {
        let snapshot = model.snapshot
        return LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
            QuickActionTile(
                title: "home.sendClipboard",
                subtitle: snapshot.phoneClipboardPreview.map { Text("home.clipboard.preview \($0)") } ?? Text("home.sendClipboard.hint"),
                icon: .clipboard,
                color: .bloom
            ) {
                model.sendClipboard()
            }
            .disabled(!snapshot.isConnected)

            QuickActionTile(
                title: "home.pasteFromPC",
                subtitle: snapshot.pcClipboardPreview.map { Text(verbatim: $0) } ?? Text("home.pasteFromPC.empty"),
                icon: .paste,
                color: .dusk
            ) {
                model.pasteFromPC()
            }
            .disabled(!snapshot.isConnected)

            QuickActionTile(
                title: "home.sendPhotos",
                subtitle: Text("home.sendPhotos.ready \(snapshot.readyPhotoCount)"),
                icon: .photo,
                color: .leaf
            ) {
                model.sendPhotos()
            }
            .disabled(!snapshot.isConnected)

            QuickActionTile(
                title: "home.camera",
                subtitle: cameraSubtitle,
                icon: .camera,
                color: .ink
            ) {
                // The PC starts the camera; until it does, the screen says how.
                model.isCameraReadyPresented = true
            }
            .disabled(!snapshot.isConnected)
        }
    }
}

/// Shown on the home screen until a PC is paired.
struct AddPCCard: View {
    let action: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 14) {
                LaptopIllustration()
                VStack(alignment: .leading, spacing: 2) {
                    Text("home.addPC.title")
                        .font(.title3.weight(.semibold))
                        .foregroundStyle(.textPrimary)
                    Text("home.addPC.message")
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                }
            }
            Button("home.addPC.button", action: action)
                .buttonStyle(PrimaryPillButtonStyle())
        }
        .padding(18)
        .fenpoCard()
    }
}

#Preview("Light · Türkçe") {
    HomeView().sampleModel().preview(.light, "tr")
}

#Preview("Dark · Türkçe") {
    HomeView().sampleModel().preview(.dark, "tr")
}

#Preview("Light · English") {
    HomeView().sampleModel().preview(.light, "en")
}

#Preview("Dark · English, not paired") {
    HomeView().sampleModel(paired: false).preview(.dark, "en")
}

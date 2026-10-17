import PhotosUI
import SwiftUI

/// Tab bar with Fenpo, Transfers, PC and Settings, plus the pairing flow on top.
struct RootView: View {
    @Environment(AppModel.self) private var model
    @State private var pickedItems: [PhotosPickerItem] = []

    var body: some View {
        @Bindable var model = model

        TabView(selection: $model.selectedTab) {
            Tab("tab.home", systemImage: "house", value: AppModel.Tab.home) {
                HomeView()
            }
            Tab("tab.transfers", systemImage: "arrow.up.arrow.down", value: AppModel.Tab.transfers) {
                TransfersView()
            }
            Tab("tab.pc", systemImage: "display", value: AppModel.Tab.pc) {
                PCView()
            }
            Tab("tab.settings", systemImage: "gearshape", value: AppModel.Tab.settings) {
                SettingsView()
            }
        }
        .overlay(alignment: .bottom) {
            if let toast = model.toast {
                ToastView(toast: toast)
                    .padding(.bottom, 96)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.snappy, value: model.toast?.id)
        // Only the close button and the PC end the camera; SwiftUI writing false here (for example
        // when a presentation is busy) must not stop it.
        .fullScreenCover(isPresented: Binding(
            get: { model.cameraState != nil || model.isCameraReadyPresented },
            set: { _ in }
        )) {
            if let state = model.cameraState {
                CameraModeView(state: state, session: model.cameraSession, actions: model.cameraActions)
            } else {
                CameraReadyView {
                    model.isCameraReadyPresented = false
                }
            }
        }
        .fullScreenCover(isPresented: $model.isRinging) {
            RingingView(pcName: model.snapshot.pc?.name) {
                model.stopRinging()
            }
        }
        .photosPicker(
            isPresented: Bindable(model).isPhotoPickerPresented,
            selection: $pickedItems,
            maxSelectionCount: 50,
            matching: .any(of: [.images, .videos])
        )
        .onChange(of: pickedItems) { _, items in
            guard !items.isEmpty else {
                return
            }
            pickedItems = []
            Task {
                var urls: [URL] = []
                for item in items {
                    if let file = try? await item.loadTransferable(type: PickedFile.self) {
                        urls.append(file.url)
                    }
                }
                model.sendFiles(urls)
            }
        }
        .fullScreenCover(isPresented: $model.isPairingPresented) {
            PairingFlowView()
        }
    }
}

#Preview("Light · English") {
    RootView().sampleModel().preview(.light, "en")
}

#Preview("Dark · Türkçe") {
    RootView().sampleModel().preview(.dark, "tr")
}

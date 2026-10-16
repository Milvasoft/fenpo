import SwiftUI

/// Tab bar with Fenpo, Transfers, PC and Settings, plus the pairing flow on top.
struct RootView: View {
    @Environment(AppModel.self) private var model

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

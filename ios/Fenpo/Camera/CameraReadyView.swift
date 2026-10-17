import SwiftUI

/// The camera screen before the PC asks for the camera: like a Mac's Continuity Camera, the PC
/// starts it, so this says how. When the PC's CameraStart arrives, the live camera screen takes over.
struct CameraReadyView: View {
    let close: () -> Void

    var body: some View {
        ZStack {
            LinearGradient(colors: [.haze, .dusk], startPoint: .top, endPoint: .bottom)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                HStack {
                    GlassCircleButton(systemImage: "xmark", label: "camera.close", action: close)
                    Spacer()
                }
                .padding(.horizontal, 16)
                .padding(.top, 8)

                Spacer()

                VStack(spacing: 14) {
                    Image(systemName: "web.camera")
                        .font(.system(size: 48))
                        .foregroundStyle(.textPrimary)
                    Text("camera.ready.title")
                        .font(.title3.weight(.semibold))
                    Text("camera.ready.message")
                        .font(.subheadline)
                        .foregroundStyle(.textSecondary)
                    ProgressView()
                        .padding(.top, 6)
                }
                .multilineTextAlignment(.center)
                .foregroundStyle(.textPrimary)
                .padding(24)
                .frame(maxWidth: .infinity)
                .fenpoGlass(in: .rect(cornerRadius: 34, style: .continuous))
                .padding(.horizontal, 16)

                Spacer()
            }
        }
        .environment(\.colorScheme, .dark)
    }
}

#Preview("Türkçe") {
    CameraReadyView {}
        .preview(.dark, "tr")
}

#Preview("English") {
    CameraReadyView {}
        .preview(.light, "en")
}

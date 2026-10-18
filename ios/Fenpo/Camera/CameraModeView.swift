import AVFoundation
import FenpoApple
import FenpoCore
import SwiftUI

/// "Kamera modu" from the design: the live picture, who uses it, lens and option buttons, and a
/// way to darken the screen to save battery. Always dark, like the camera.
struct CameraModeView: View {
    let state: CameraService.UIState
    let session: AVCaptureSession?
    let actions: CameraActions

    @State private var isDimmed = false

    var body: some View {
        ZStack {
            preview
                .ignoresSafeArea()

            VStack(spacing: 0) {
                HStack(spacing: 8) {
                    GlassCircleButton(systemImage: "xmark", label: "camera.stop", action: actions.stop)
                    Spacer()
                    pill {
                        Circle()
                            .fill(.red)
                            .frame(width: 8, height: 8)
                        Text(state.consumer.isEmpty ? "camera.live" : "camera.live.consumer \(state.consumer)")
                    }
                    pill {
                        Text("link.wifi")
                    }
                }
                .padding(.horizontal, 16)
                .padding(.top, 8)

                Spacer()

                controls
                    .padding(.horizontal, 12)
                    .padding(.bottom, 12)
            }

            if isDimmed {
                Color.black
                    .ignoresSafeArea()
                    .overlay(alignment: .bottom) {
                        Text("camera.dimmed.hint")
                            .font(.footnote)
                            .foregroundStyle(.textTertiary)
                            .padding(.bottom, 40)
                    }
                    .onTapGesture {
                        isDimmed = false
                    }
            }
        }
        .environment(\.colorScheme, .dark)
        .statusBarHidden(isDimmed)
    }

    @ViewBuilder
    private var preview: some View {
        if let session {
            CameraPreview(session: session)
        } else {
            LinearGradient(colors: [.haze, .dusk], startPoint: .top, endPoint: .bottom)
        }
    }

    private var controls: some View {
        VStack(spacing: 12) {
            HStack(spacing: 8) {
                ForEach(state.zoomPresets, id: \.self) { preset in
                    Button {
                        actions.zoom(preset)
                    } label: {
                        Text(zoomLabel(preset))
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(state.zoom == preset ? Color.ink : Color.textPrimary)
                            .frame(width: 40, height: 40)
                            .background(state.zoom == preset ? Color.onAccent.opacity(0.9) : Color.textPrimary.opacity(0.12), in: .circle)
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(state.zoom == preset ? .isSelected : [])
                }
            }

            OptionButton(title: "camera.flip", systemImage: "arrow.triangle.2.circlepath.camera", isOn: false, action: actions.flip)
                .disabled(!state.frontAvailable)

            Button("camera.dim") {
                isDimmed = true
            }
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.textPrimary)
            .frame(maxWidth: .infinity, minHeight: 44)
            .background(Color.textPrimary.opacity(0.12), in: .capsule)
            .buttonStyle(.plain)
        }
        .padding(14)
        .fenpoGlass(in: .rect(cornerRadius: 34, style: .continuous))
    }

    private func pill<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        HStack(spacing: 8) {
            content()
        }
        .font(.subheadline.weight(.semibold))
        .foregroundStyle(.textPrimary)
        .padding(.horizontal, 14)
        .frame(height: 36)
        .fenpoGlass(in: .capsule)
    }

    private func zoomLabel(_ ratio: Float) -> String {
        let formatted = ratio.formatted(.number.precision(.fractionLength(0...1)))
        return ratio == 1 ? "\(formatted)×" : formatted
    }
}

/// What the camera screen can ask for.
struct CameraActions {
    var stop: () -> Void = {}
    var zoom: (Float) -> Void = { _ in }
    var flip: () -> Void = {}
}

/// A pill button with an icon, filled in the brand color when on.
private struct OptionButton: View {
    let title: LocalizedStringKey
    let systemImage: String
    let isOn: Bool
    let action: () -> Void

    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: systemImage)
                .font(.footnote.weight(.semibold))
                .foregroundStyle(isOn ? Color.onAccent : Color.textPrimary)
                .frame(maxWidth: .infinity, minHeight: 50)
                .background(isOn ? Color.bloom : Color.textPrimary.opacity(0.12), in: .capsule)
                .opacity(isEnabled ? 1 : 0.45)
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isOn ? .isSelected : [])
    }
}

/// The capture session's live picture.
private struct CameraPreview: UIViewRepresentable {
    let session: AVCaptureSession

    func makeUIView(context: Context) -> PreviewView {
        let view = PreviewView()
        view.previewLayer.session = session
        view.previewLayer.videoGravity = .resizeAspectFill
        return view
    }

    func updateUIView(_ view: PreviewView, context: Context) {
        view.previewLayer.session = session
    }

    final class PreviewView: UIView {
        override class var layerClass: AnyClass {
            AVCaptureVideoPreviewLayer.self
        }

        var previewLayer: AVCaptureVideoPreviewLayer {
            // swiftlint:disable:next force_cast
            layer as! AVCaptureVideoPreviewLayer
        }
    }
}

#Preview("Türkçe") {
    CameraModeView(state: .sample, session: nil, actions: CameraActions())
        .preview(.dark, "tr")
}

#Preview("English") {
    CameraModeView(state: .sample, session: nil, actions: CameraActions())
        .preview(.light, "en")
}

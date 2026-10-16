import FenpoApple
import SwiftUI
import UIKit

/// "Scan the code on your computer": camera with a finder, close and torch buttons, and a glass sheet.
/// Always dark, like the camera.
struct ScannerView: View {
    let onCode: (String) -> Void
    let onClose: () -> Void

    @State private var camera: CameraAccess.State = .checking
    @State private var isTorchOn = false
    @Environment(\.openURL) private var openURL

    var body: some View {
        GeometryReader { proxy in
            ZStack {
                background
                    .ignoresSafeArea()

                ScannerFinder()
                    .stroke(.onAccent, style: StrokeStyle(lineWidth: 5, lineCap: .round, lineJoin: .round))
                    .frame(width: 230, height: 230)
                    .position(x: proxy.size.width / 2, y: proxy.size.height * 0.4)
                    .accessibilityHidden(true)

                VStack(spacing: 0) {
                    topBar
                    Spacer()
                    sheet
                }
            }
        }
        .environment(\.colorScheme, .dark)
        .task {
            startBrowsing()
            camera = await CameraAccess.prepare()
        }
        .onDisappear {
            FenpoServices.shared.browser.stop()
            if isTorchOn {
                CameraAccess.setTorch(false)
            }
        }
    }

    /// Bonjour while scanning brings up the local network prompt before the first TLS connection.
    private func startBrowsing() {
        #if DEBUG
        if ScreenshotScene.current != nil {
            return
        }
        #endif
        FenpoServices.shared.browser.start()
    }

    @ViewBuilder
    private var background: some View {
        if camera == .ready {
            QRCodeScanner(onCode: onCode)
        } else {
            RadialGradient(colors: [.scannerCenter, .scannerEdge], center: UnitPoint(x: 0.5, y: 0.45), startRadius: 0, endRadius: 420)
        }
    }

    private var topBar: some View {
        HStack {
            GlassCircleButton(systemImage: "xmark", label: "common.close", action: onClose)
            Spacer()
            if camera == .ready && CameraAccess.hasTorch {
                GlassCircleButton(systemImage: isTorchOn ? "flashlight.on.fill" : "flashlight.off.fill", label: "pairing.torch") {
                    isTorchOn.toggle()
                    CameraAccess.setTorch(isTorchOn)
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 4)
    }

    private var sheet: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("pairing.scan.title")
                .font(.title2.weight(.semibold))
                .foregroundStyle(.textPrimary)
            Text(camera == .denied ? "pairing.camera.denied" : "pairing.scan.message")
                .foregroundStyle(.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            if camera == .denied {
                Button("common.openSettings") {
                    if let url = URL(string: UIApplication.openSettingsURLString) {
                        openURL(url)
                    }
                }
                .buttonStyle(PrimaryPillButtonStyle())
                .padding(.top, 12)
            } else {
                // No numeric fallback (DECISIONS #39): the PC shows the code again instead.
                Text("pairing.scan.hint")
                    .font(.footnote)
                    .foregroundStyle(.textTertiary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 10)
            }
        }
        .padding(.horizontal, 24)
        .padding(.top, 26)
        .padding(.bottom, 30)
        .frame(maxWidth: .infinity, alignment: .leading)
        .fenpoGlass(in: .rect(cornerRadius: 44, style: .continuous))
        .padding(8)
    }
}

/// Round glass button used over the camera.
struct GlassCircleButton: View {
    let systemImage: String
    let label: LocalizedStringKey
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: systemImage)
                .font(.title3)
                .foregroundStyle(.textPrimary)
                .frame(width: 44, height: 44)
                .fenpoGlass(in: .circle)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
    }
}

/// Four rounded corner brackets of the QR finder.
struct ScannerFinder: Shape {
    var arm: CGFloat = 46
    var radius: CGFloat = 22

    func path(in rect: CGRect) -> Path {
        var path = Path()
        let corners: [(CGPoint, CGFloat, CGFloat)] = [
            (CGPoint(x: rect.minX, y: rect.minY), 1, 1),
            (CGPoint(x: rect.maxX, y: rect.minY), -1, 1),
            (CGPoint(x: rect.maxX, y: rect.maxY), -1, -1),
            (CGPoint(x: rect.minX, y: rect.maxY), 1, -1),
        ]
        for (corner, dx, dy) in corners {
            path.move(to: CGPoint(x: corner.x, y: corner.y + dy * arm))
            path.addLine(to: CGPoint(x: corner.x, y: corner.y + dy * radius))
            path.addQuadCurve(to: CGPoint(x: corner.x + dx * radius, y: corner.y), control: corner)
            path.addLine(to: CGPoint(x: corner.x + dx * arm, y: corner.y))
        }
        return path
    }
}

#Preview("Türkçe") {
    ScannerView(onCode: { _ in }, onClose: {})
        .preview(.light, "tr")
}

#Preview("English") {
    ScannerView(onCode: { _ in }, onClose: {})
        .preview(.dark, "en")
}

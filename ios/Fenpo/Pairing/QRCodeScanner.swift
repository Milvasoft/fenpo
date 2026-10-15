import AVFoundation
import SwiftUI
import VisionKit

/// Live camera that reports the first QR code it recognizes.
struct QRCodeScanner: UIViewControllerRepresentable {
    let onCode: (String) -> Void

    /// The device has a camera that VisionKit can scan with, and access is granted.
    @MainActor
    static var isAvailable: Bool {
        DataScannerViewController.isSupported && DataScannerViewController.isAvailable
    }

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(
            recognizedDataTypes: [.barcode(symbologies: [.qr])],
            qualityLevel: .balanced,
            recognizesMultipleItems: false,
            isHighFrameRateTrackingEnabled: false,
            isPinchToZoomEnabled: true,
            isGuidanceEnabled: false,
            isHighlightingEnabled: false
        )
        scanner.delegate = context.coordinator
        return scanner
    }

    func updateUIViewController(_ scanner: DataScannerViewController, context: Context) {
        if !scanner.isScanning {
            try? scanner.startScanning()
        }
    }

    static func dismantleUIViewController(_ scanner: DataScannerViewController, coordinator: Coordinator) {
        scanner.stopScanning()
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(onCode: onCode)
    }

    @MainActor
    final class Coordinator: NSObject {
        private let onCode: (String) -> Void
        private var delivered = false

        init(onCode: @escaping (String) -> Void) {
            self.onCode = onCode
        }

        fileprivate func handle(_ items: [RecognizedItem]) {
            guard !delivered else {
                return
            }
            for item in items {
                if case .barcode(let barcode) = item, let payload = barcode.payloadStringValue {
                    delivered = true
                    onCode(payload)
                    return
                }
            }
        }
    }
}

extension QRCodeScanner.Coordinator: @preconcurrency DataScannerViewControllerDelegate {
    func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
        handle(addedItems)
    }
}

/// Camera permission and torch helpers for the scanner.
enum CameraAccess {
    /// Camera state as the scanner screen shows it.
    enum State {
        case checking
        case ready
        case denied
        case unavailable
    }

    /// Asks for camera access when needed and reports whether scanning can start.
    @MainActor
    static func prepare() async -> State {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .notDetermined:
            let granted = await AVCaptureDevice.requestAccess(for: .video)
            return granted ? availability : .denied
        case .authorized:
            return availability
        default:
            return .denied
        }
    }

    @MainActor
    private static var availability: State {
        QRCodeScanner.isAvailable ? .ready : .unavailable
    }

    /// The back camera has a torch.
    static var hasTorch: Bool {
        AVCaptureDevice.default(for: .video)?.hasTorch ?? false
    }

    /// Turns the torch on or off; does nothing without a torch.
    /// - Parameter isOn: Desired torch state.
    static func setTorch(_ isOn: Bool) {
        guard let device = AVCaptureDevice.default(for: .video), device.hasTorch else {
            return
        }
        do {
            try device.lockForConfiguration()
            device.torchMode = isOn ? .on : .off
            device.unlockForConfiguration()
        } catch {
            return
        }
    }
}

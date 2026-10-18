#if os(iOS)
import FenpoCore
import Foundation
import UIKit
import UniformTypeIdentifiers

/// Reads the iPhone's clipboard for sending to the PC.
///
/// iOS lets only the app in front read the pasteboard, and shows "Allow Paste" for every read unless
/// Settings › Apps › Fenpo › Paste from Other Apps is set to Allow. Metadata (`changeCount`, the
/// `has…` flags, the types and pattern detection) comes without that prompt, so Fenpo decides with
/// metadata first and reads the content once. The system Paste button (`PasteButton`) hands the
/// content over without a prompt at all.
@MainActor
public enum PhoneClipboard {
    /// Called after Fenpo dealt with the current pasteboard (sent, offered, dismissed or wrote it),
    /// so the app can drop an offer that an intent already handled.
    public static var onHandled: (() -> Void)?

    private static let log = FenpoLog("Clipboard")

    /// Changes whenever anything is copied; no prompt.
    public static var changeCount: Int {
        UIPasteboard.general.changeCount
    }

    /// The pasteboard has text, a link or an image; no prompt.
    public static var hasContent: Bool {
        let pasteboard = UIPasteboard.general
        return pasteboard.hasStrings || pasteboard.hasURLs || pasteboard.hasImages
    }

    /// The app it came from marked the content private (a password manager); no prompt.
    public static var isMarkedSensitive: Bool {
        ClipboardSensitivity.isMarked(types: UIPasteboard.general.types)
    }

    /// Whether the system sees a number on the pasteboard, which may be a one-time code. Uses
    /// pattern detection, which needs no paste prompt.
    public static func mightHoldCode() async -> Bool {
        await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
            let patterns: Set<UIPasteboard.DetectionPattern> = [.number]
            UIPasteboard.general.detectPatterns(for: patterns) { @Sendable result in
                let found = (try? result.get()) ?? []
                continuation.resume(returning: found.contains(.number))
            }
        }
    }

    /// Records that Fenpo dealt with the pasteboard as it is now.
    public static func markHandled() {
        ClipboardOnOpen.markHandled(changeCount, in: UserDefaults(suiteName: FenpoIdentifiers.appGroup))
        onHandled?()
    }

    /// Reads the clipboard: an image, else a link, else text. May show the paste prompt; reads one
    /// representation only, so the prompt shows at most once.
    /// - Returns: The payload, or `nil` when there is nothing to send or the user declined.
    public static func read() -> ClipboardPayload? {
        let pasteboard = UIPasteboard.general
        let sensitive = isMarkedSensitive
        let payload: ClipboardPayload?
        if pasteboard.hasImages {
            payload = pasteboard.image.flatMap { ClipboardImageEncoder.encode($0) }.map { ClipboardPayload(.image($0.data, mimeType: $0.mimeType), isSensitive: sensitive) }
        } else if pasteboard.hasURLs, let url = pasteboard.url, !url.isFileURL {
            payload = ClipboardPayload(.url(url), isSensitive: sensitive)
        } else if pasteboard.hasStrings, let text = pasteboard.string, !text.isEmpty {
            payload = ClipboardPayload(.text(text), isSensitive: sensitive)
        } else {
            payload = nil
        }
        log.info("Read the iPhone clipboard: \(payload?.logDescription ?? "nothing usable"); changeCount \(pasteboard.changeCount)")
        return payload
    }

    /// The content types the Paste button accepts.
    public static let pasteTypes: [UTType] = [.url, .image, .plainText]

    /// Turns what the system Paste button handed over into a payload.
    /// - Parameters:
    ///   - providers: Item providers from `PasteButton`.
    ///   - isSensitive: The pasteboard was marked private (read before the tap).
    public static func payload(from providers: [NSItemProvider], isSensitive: Bool) async -> ClipboardPayload? {
        for provider in providers {
            let types = provider.registeredContentTypes
            if let type = types.first(where: { $0.conforms(to: .image) }),
               let data = await loadData(provider, type: type),
               let image = UIImage(data: data),
               let encoded = ClipboardImageEncoder.encode(image, original: data, type: type) {
                return ClipboardPayload(.image(encoded.data, mimeType: encoded.mimeType), isSensitive: isSensitive)
            }
            if types.contains(where: { $0.conforms(to: .url) && !$0.conforms(to: .fileURL) }),
               let data = await loadData(provider, type: .url),
               let url = URL(dataRepresentation: data, relativeTo: nil), !url.isFileURL {
                return ClipboardPayload(.url(url), isSensitive: isSensitive)
            }
            let textType: UTType? = types.contains(.utf8PlainText) ? .utf8PlainText : types.first { $0.conforms(to: .plainText) }
            if let textType, let data = await loadData(provider, type: textType),
               let text = String(data: data, encoding: .utf8) ?? String(data: data, encoding: .utf16), !text.isEmpty {
                return ClipboardPayload(.text(text), isSensitive: isSensitive)
            }
        }
        log.info("The Paste button handed over nothing usable (\(providers.count) item(s))")
        return nil
    }

    private static func loadData(_ provider: NSItemProvider, type: UTType) async -> Data? {
        await withCheckedContinuation { (continuation: CheckedContinuation<Data?, Never>) in
            _ = provider.loadDataRepresentation(for: type) { @Sendable data, _ in
                continuation.resume(returning: data)
            }
        }
    }
}

/// Fits a clipboard image into a `ClipboardItem`: PNG as it is when small enough, otherwise JPEG,
/// scaled down until it fits `ClipboardPayload.maxImageBytes`.
@MainActor
public enum ClipboardImageEncoder {
    /// Encodes an image.
    /// - Parameters:
    ///   - image: The image.
    ///   - original: Bytes it came from, reused when they are PNG or JPEG and small enough.
    ///   - type: Type of `original`.
    /// - Returns: Bytes and MIME type, or `nil` when it cannot be encoded.
    public static func encode(_ image: UIImage, original: Data? = nil, type: UTType? = nil) -> (data: Data, mimeType: String)? {
        let limit = ClipboardPayload.maxImageBytes
        if let original, original.count <= limit, let type {
            if type.conforms(to: .png) {
                return (original, "image/png")
            }
            if type.conforms(to: .jpeg) {
                return (original, "image/jpeg")
            }
        }
        if let png = image.pngData(), png.count <= limit {
            return (png, "image/png")
        }
        var current = image
        for _ in 0..<6 {
            for quality in [0.85, 0.7, 0.55] {
                if let jpeg = current.jpegData(compressionQuality: quality), jpeg.count <= limit {
                    return (jpeg, "image/jpeg")
                }
            }
            let size = CGSize(width: current.size.width * 0.7, height: current.size.height * 0.7)
            guard size.width >= 64, size.height >= 64 else {
                break
            }
            let format = UIGraphicsImageRendererFormat.default()
            format.scale = 1
            let source = current
            current = UIGraphicsImageRenderer(size: size, format: format).image { _ in
                source.draw(in: CGRect(origin: .zero, size: size))
            }
        }
        return nil
    }
}
#endif

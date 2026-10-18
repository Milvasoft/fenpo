import FenpoCore
import FenpoProtocol
import Foundation
import Testing

struct ClipboardPayloadTests {
    @Test(arguments: ["482913", "482 913", "1234-5678", " 0042\n"])
    func oneTimeCodesAreSensitive(text: String) {
        #expect(ClipboardSensitivity.looksLikeOneTimeCode(text))
        #expect(ClipboardPayload(.text(text)).isSensitive)
    }

    @Test(arguments: ["123", "123456789", "merhaba 1234", "12.34", "", "١٢٣٤٥٦"])
    func otherTextIsNotACode(text: String) {
        #expect(!ClipboardSensitivity.looksLikeOneTimeCode(text))
        #expect(!ClipboardPayload(.text(text)).isSensitive)
    }

    @Test
    func markerTypesMakeItSensitive() {
        #expect(ClipboardSensitivity.isMarked(types: ["public.utf8-plain-text", "org.nspasteboard.ConcealedType"]))
        #expect(!ClipboardSensitivity.isMarked(types: ["public.utf8-plain-text"]))
    }

    @Test
    func itemCarriesContentAndSensitivity() throws {
        let text = ClipboardPayload(.text("merhaba"), isSensitive: true).item(id: "a")
        #expect(text.itemID == "a")
        #expect(text.text == "merhaba")
        #expect(text.sensitive)

        let url = ClipboardPayload(.url(try #require(URL(string: "https://fenpo.app")))).item()
        #expect(url.url == "https://fenpo.app")
        #expect(!url.sensitive)

        let image = ClipboardPayload(.image(Data([1, 2, 3]), mimeType: "image/png")).item()
        #expect(image.image.mimeType == "image/png")
        #expect(image.image.data == Data([1, 2, 3]))
    }

    @Test
    func logDescriptionHasNoContent() {
        let payload = ClipboardPayload(.text("gizli parola"), isSensitive: true)
        #expect(!payload.logDescription.contains("gizli"))
        #expect(payload.logDescription == "text, 12 characters, sensitive")
    }

    @Test
    func emptyContentIsEmpty() {
        #expect(ClipboardPayload(.text("")).isEmpty)
        #expect(ClipboardPayload(.image(Data(), mimeType: "image/png")).isEmpty)
        #expect(!ClipboardPayload(.text("a")).isEmpty)
    }
}

struct ClipboardOnOpenTests {
    private func decide(changeCount: Int = 5, handled: Int? = 4, hasContent: Bool = true, sensitive: Bool = false, connected: Bool = true, sharing: Bool = true, mode: ClipboardOnOpen.Mode) -> ClipboardOnOpen.Decision {
        ClipboardOnOpen.decide(changeCount: changeCount, handled: handled, hasContent: hasContent, mightBeSensitive: sensitive, isConnected: connected, sharingEnabled: sharing, mode: mode)
    }

    @Test
    func newClipboardIsOfferedOrSent() {
        #expect(decide(mode: .ask) == .offer)
        #expect(decide(mode: .automatic) == .send)
        #expect(decide(mode: .off) == .nothing)
    }

    @Test
    func sensitiveClipboardIsNeverSentWithoutATap() {
        #expect(decide(sensitive: true, mode: .automatic) == .offer)
        #expect(decide(sensitive: true, mode: .ask) == .offer)
    }

    @Test
    func handledOrUnusableClipboardIsLeftAlone() {
        #expect(decide(handled: 5, mode: .automatic) == .nothing)
        #expect(decide(handled: nil, mode: .automatic) == .nothing)
        #expect(decide(hasContent: false, mode: .automatic) == .nothing)
        #expect(decide(connected: false, mode: .automatic) == .nothing)
        #expect(decide(sharing: false, mode: .automatic) == .nothing)
    }

    @Test
    func settingsLiveInDefaults() throws {
        let defaults = try #require(UserDefaults(suiteName: "ClipboardOnOpenTests.\(UUID().uuidString)"))
        #expect(ClipboardOnOpen.mode(in: defaults) == .ask)
        #expect(ClipboardOnOpen.isSharingEnabled(in: defaults))
        #expect(ClipboardOnOpen.handledChangeCount(in: defaults) == nil)

        defaults.set(ClipboardOnOpen.Mode.automatic.rawValue, forKey: ClipboardOnOpen.modeKey)
        defaults.set(false, forKey: ClipboardOnOpen.sharingKey)
        ClipboardOnOpen.markHandled(12, in: defaults)
        #expect(ClipboardOnOpen.mode(in: defaults) == .automatic)
        #expect(!ClipboardOnOpen.isSharingEnabled(in: defaults))
        #expect(ClipboardOnOpen.handledChangeCount(in: defaults) == 12)
    }
}

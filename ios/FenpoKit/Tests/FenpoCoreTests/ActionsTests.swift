import FenpoCore
import Foundation
import Testing

struct ActionsTests {
    @Test
    func registryReportsUnavailableUntilTheAppRegisters() throws {
        // The shared registry is process-wide; this is the only test that touches it.
        let registry = FenpoActionsRegistry.shared
        if registry.current == nil {
            #expect(throws: FenpoActionError.unavailableHere) {
                try registry.require()
            }
        }
        registry.register(SampleActions())
        #expect(registry.current != nil)
        _ = try registry.require()
    }

    @Test
    func sampleActionsRejectEmptyText() async throws {
        let actions = SampleActions()
        await #expect(throws: FenpoActionError.emptyText) {
            try await actions.sendText("")
        }
        try await actions.sendText("merhaba")
        #expect(try await actions.fetchPCClipboard().isEmpty == false)
    }
}

struct FocusSyncTests {
    @Test
    func switchIsOnUntilTurnedOff() throws {
        let defaults = try #require(UserDefaults(suiteName: "FocusSyncTests.\(UUID().uuidString)"))
        #expect(FocusSync.isEnabled(in: defaults))
        defaults.set(false, forKey: FocusSync.enabledKey)
        #expect(!FocusSync.isEnabled(in: defaults))
        #expect(FocusSync.isEnabled(in: nil))
    }

    @Test
    func reportsFocusOnlyWhenBothAreOn() {
        #expect(FocusSync.reportedState(filterActive: true, enabled: true))
        #expect(!FocusSync.reportedState(filterActive: true, enabled: false))
        #expect(!FocusSync.reportedState(filterActive: false, enabled: true))
    }
}

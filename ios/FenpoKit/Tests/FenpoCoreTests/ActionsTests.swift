import FenpoCore
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

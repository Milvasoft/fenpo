/// Actions that succeed with sample data, for previews and screenshot scenes.
public struct SampleActions: FenpoActions {
    /// Creates sample actions.
    public init() {
    }

    public func sendText(_ text: String) async throws {
        guard !text.isEmpty else {
            throw FenpoActionError.emptyText
        }
    }

    public func fetchPCClipboard() async throws -> String {
        SampleSession.pairedSnapshot().pcClipboardPreview ?? ""
    }

    public func lockPC() async throws {
    }
}

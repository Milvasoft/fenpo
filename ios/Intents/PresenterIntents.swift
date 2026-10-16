import AppIntents
import FenpoCore

/// The next slide on the PC, for Shortcuts and the Action button.
struct NextSlideIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.nextSlide.title"
    static let description = IntentDescription("intent.nextSlide.description")

    func perform() async throws -> some IntentResult {
        try await IntentSupport.actions(for: Self.self).presenter(.next)
        return .result()
    }
}

/// The previous slide on the PC.
struct PreviousSlideIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.previousSlide.title"
    static let description = IntentDescription("intent.previousSlide.description")

    func perform() async throws -> some IntentResult {
        try await IntentSupport.actions(for: Self.self).presenter(.previous)
        return .result()
    }
}

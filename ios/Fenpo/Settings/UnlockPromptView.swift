import FenpoApple
import SwiftUI

/// The PC asks for Face ID: to set up unlocking, or to unlock its locked session now.
struct UnlockPromptView: View {
    let prompt: UnlockService.Prompt
    let approve: () -> Void
    let decline: () -> Void

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "lock.open.laptopcomputer")
                .font(.system(size: 44))
                .foregroundStyle(.accent)
                .padding(.top, 28)
            title
                .font(.title3.weight(.semibold))
                .multilineTextAlignment(.center)
            message
                .font(.subheadline)
                .foregroundStyle(.textSecondary)
                .multilineTextAlignment(.center)
            Spacer(minLength: 0)
            Button(action: approve) {
                Label("unlock.approve", systemImage: "faceid")
            }
            .buttonStyle(PrimaryPillButtonStyle())
            Button("unlock.decline", action: decline)
                .font(.body.weight(.semibold))
                .foregroundStyle(.textPrimary)
                .padding(.bottom, 8)
        }
        .padding(.horizontal, 24)
        .padding(.bottom, 16)
        .foregroundStyle(.textPrimary)
        .presentationDetents([.medium])
        .interactiveDismissDisabled()
    }

    private var title: Text {
        switch prompt.kind {
        case .enroll:
            Text("unlock.enroll.title \(prompt.pcName)")
        case .unlock:
            Text("unlock.title \(prompt.pcName)")
        }
    }

    private var message: Text {
        switch prompt.kind {
        case .enroll:
            Text("unlock.enroll.message")
        case .unlock:
            Text("unlock.message")
        }
    }
}

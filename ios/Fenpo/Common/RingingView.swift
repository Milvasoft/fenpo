import SwiftUI

/// Shown while the PC rings this iPhone and Fenpo is in front.
struct RingingView: View {
    let pcName: String?
    let stop: () -> Void

    @State private var isPulsing = false

    private var message: Text {
        if let pcName {
            Text("ring.message \(pcName)")
        } else {
            Text("ring.message.unknown")
        }
    }

    var body: some View {
        VStack(spacing: 24) {
            Spacer()
            ZStack {
                Circle()
                    .fill(Color.bloom.opacity(0.25))
                    .frame(width: 180, height: 180)
                    .scaleEffect(isPulsing ? 1.15 : 0.9)
                Image(systemName: "bell.and.waves.left.and.right.fill")
                    .font(.system(size: 56, weight: .semibold))
                    .foregroundStyle(.onAccent)
                    .frame(width: 120, height: 120)
                    .background(.bloom, in: .circle)
            }
            .accessibilityHidden(true)

            VStack(spacing: 6) {
                Text("ring.title")
                    .font(.largeTitle.weight(.bold))
                    .foregroundStyle(.textPrimary)
                message
                    .font(.body)
                    .foregroundStyle(.textSecondary)
                    .multilineTextAlignment(.center)
            }
            Spacer()
            Button("ring.stop", action: stop)
                .buttonStyle(PrimaryPillButtonStyle())
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(.canvas)
        .onAppear {
            withAnimation(.easeInOut(duration: 0.8).repeatForever(autoreverses: true)) {
                isPulsing = true
            }
        }
    }
}

#Preview("Light · Türkçe") {
    RingingView(pcName: "BUGRA-PC") {}.preview(.light, "tr")
}

#Preview("Dark · English") {
    RingingView(pcName: nil) {}.preview(.dark, "en")
}

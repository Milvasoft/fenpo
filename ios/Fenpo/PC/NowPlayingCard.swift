import FenpoCore
import SwiftUI
import UIKit

/// "Şu an çalıyor": what plays on the PC, with volume, previous, play/pause and next. Shown only
/// while the PC has a player that plays or is paused.
struct NowPlayingCard: View {
    let media: PCMedia
    let send: (PCMediaCommand) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 12) {
                artwork
                VStack(alignment: .leading, spacing: 2) {
                    Text(verbatim: media.displayTitle)
                        .font(.headline)
                        .foregroundStyle(.textPrimary)
                        .lineLimit(1)
                    if !media.artist.isEmpty {
                        Text(verbatim: media.artist)
                            .font(.subheadline)
                            .foregroundStyle(.textSecondary)
                            .lineLimit(1)
                    }
                    if !media.title.isEmpty, !media.appName.isEmpty {
                        Text(verbatim: media.appName)
                            .font(.caption)
                            .foregroundStyle(.textTertiary)
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 0)
            }

            if media.durationMs > 0 {
                TimelineView(.periodic(from: .now, by: 1)) { context in
                    ProgressView(value: Double(media.position(at: context.date)), total: Double(media.durationMs))
                        .tint(.bloom)
                }
                .accessibilityHidden(true)
            }

            HStack {
                MediaButton(systemImage: "speaker.wave.1.fill", label: "media.volumeDown") {
                    send(.volume(media.volumeStep(up: false)))
                }
                Spacer()
                MediaButton(systemImage: "backward.fill", label: "media.previous") {
                    send(.previous)
                }
                .disabled(!media.canPrevious)
                Spacer()
                MediaButton(systemImage: media.isPlaying ? "pause.fill" : "play.fill", label: media.isPlaying ? "media.pause" : "media.play", isProminent: true) {
                    send(.toggle)
                }
                Spacer()
                MediaButton(systemImage: "forward.fill", label: "media.next") {
                    send(.next)
                }
                .disabled(!media.canNext)
                Spacer()
                MediaButton(systemImage: "speaker.wave.3.fill", label: "media.volumeUp") {
                    send(.volume(media.volumeStep(up: true)))
                }
            }
        }
        .padding(16)
        .fenpoCard()
    }

    @ViewBuilder
    private var artwork: some View {
        if let data = media.artwork, let image = UIImage(data: data) {
            Image(uiImage: image)
                .resizable()
                .scaledToFill()
                .frame(width: 56, height: 56)
                .clipShape(.rect(cornerRadius: 12, style: .continuous))
                .accessibilityHidden(true)
        } else {
            Image(systemName: "music.note")
                .font(.title2)
                .foregroundStyle(.onAccent)
                .frame(width: 56, height: 56)
                .background(.dusk, in: .rect(cornerRadius: 12, style: .continuous))
                .accessibilityHidden(true)
        }
    }
}

/// A round media button.
private struct MediaButton: View {
    let systemImage: String
    let label: LocalizedStringKey
    var isProminent = false
    let action: () -> Void

    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        Button(action: action) {
            Image(systemName: systemImage)
                .font(isProminent ? .title2 : .body)
                .foregroundStyle(isProminent ? Color.onAccent : Color.textPrimary)
                .frame(width: isProminent ? 56 : 44, height: isProminent ? 56 : 44)
                .background(isProminent ? Color.bloom : Color.textPrimary.opacity(0.08), in: .circle)
                .opacity(isEnabled ? 1 : 0.35)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
    }
}

#Preview("Light · Türkçe") {
    NowPlayingCard(media: SampleSession.pairedSnapshot().pcMedia!) { _ in }
        .padding()
        .preview(.light, "tr")
}

#Preview("Dark · English") {
    NowPlayingCard(media: SampleSession.pairedSnapshot().pcMedia!) { _ in }
        .padding()
        .preview(.dark, "en")
}

import FenpoCore
import FenpoProtocol
import SwiftUI
import UIKit

/// "Sunum kumandası": next and previous slide, start, end, black screen, a laser pointer on the
/// PC's screen and keeping the PC's screen on. The iPhone's own screen stays on meanwhile.
struct PresenterView: View {
    @Environment(AppModel.self) private var model
    @State private var keepsPCAwake = false
    @State private var lastLaser = Date.distantPast
    @State private var isLaserOn = false

    var body: some View {
        VStack(spacing: 12) {
            if let problem = model.remoteProblem {
                RemoteProblemBanner(message: problem)
            }

            RemoteButton(title: "presenter.next", systemImage: "chevron.right", isProminent: true) {
                send(.presenter(.next))
            }
            .frame(maxHeight: .infinity)

            HStack(spacing: 8) {
                RemoteButton(title: "presenter.previous", systemImage: "chevron.left") {
                    send(.presenter(.previous))
                }
                RemoteButton(title: "presenter.black", systemImage: "rectangle.fill") {
                    send(.presenter(.black))
                }
            }
            .frame(height: 64)

            laserPad
                .frame(height: 150)

            HStack(spacing: 8) {
                RemoteButton(title: "presenter.start", systemImage: "play.fill") {
                    send(.presenter(.start))
                }
                RemoteButton(title: "presenter.end", systemImage: "stop.fill") {
                    send(.presenter(.end))
                }
            }
            .frame(height: 52)

            Toggle("presenter.keepAwake", isOn: $keepsPCAwake)
                .tint(.leaf)
                .padding(.horizontal, 16)
                .frame(minHeight: 50)
                .fenpoCard(cornerRadius: 22)
                .onChange(of: keepsPCAwake) { _, isOn in
                    send(.keepAwake(isOn))
                }
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 12)
        .fenpoScreenBackground()
        .navigationTitle("remote.presenter")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            model.pcInput?.begin()
            UIApplication.shared.isIdleTimerDisabled = true
        }
        .onDisappear {
            if keepsPCAwake {
                send(.keepAwake(false))
            }
            if isLaserOn {
                send(.laser(active: false))
            }
            model.pcInput?.end()
            UIApplication.shared.isIdleTimerDisabled = false
        }
    }

    /// Touch and move: the dot follows on the PC's primary screen, at the same place in the pad.
    private var laserPad: some View {
        GeometryReader { proxy in
            ZStack {
                RoundedRectangle(cornerRadius: 22, style: .continuous)
                    .fill(isLaserOn ? Color.bloom.opacity(0.15) : Color.card)
                Label("presenter.laser.hint", systemImage: "dot.radiowaves.left.and.right")
                    .font(.footnote)
                    .foregroundStyle(.textSecondary)
                    .multilineTextAlignment(.center)
                    .padding(16)
            }
            .contentShape(.rect)
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        // At most 30 dots a second; the PC draws between them.
                        guard !isLaserOn || Date().timeIntervalSince(lastLaser) >= 1.0 / 30 else {
                            return
                        }
                        isLaserOn = true
                        lastLaser = Date()
                        let x = Float(value.location.x / max(proxy.size.width, 1))
                        let y = Float(value.location.y / max(proxy.size.height, 1))
                        send(.laser(active: true, x: x, y: y))
                    }
                    .onEnded { _ in
                        isLaserOn = false
                        send(.laser(active: false))
                    }
            )
        }
    }

    private func send(_ input: Fenpo_V1_PcInput) {
        model.pcInput?.send(input)
    }
}

#Preview("Light · Türkçe") {
    NavigationStack {
        PresenterView()
    }
    .sampleModel()
    .preview(.light, "tr")
}

#Preview("Dark · English") {
    NavigationStack {
        PresenterView()
    }
    .sampleModel()
    .preview(.dark, "en")
}

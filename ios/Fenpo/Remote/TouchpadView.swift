import FenpoCore
import FenpoProtocol
import SwiftUI
import UIKit

/// "Fare ve klavye": the phone as the PC's touchpad and keyboard. One finger moves the pointer and
/// taps to click, two fingers scroll or tap for a right click, holding then moving drags, and the
/// field types on the PC.
struct TouchpadView: View {
    @Environment(AppModel.self) private var model
    @State private var typed = ""
    @State private var isResetting = false
    @FocusState private var isTyping: Bool

    /// Phone points to PC pixels, like Android's speed.
    private static let speed: CGFloat = 1.6
    /// Two-finger points per wheel notch.
    private static let pointsPerNotch: CGFloat = 15
    /// The field keeps this many characters; older ones are already on the PC.
    private static let keptCharacters = 40

    var body: some View {
        VStack(spacing: 12) {
            if let problem = model.remoteProblem {
                RemoteProblemBanner(message: problem)
            }

            TouchpadSurface(
                onMove: { dx, dy, scale in
                    model.pcInput?.move(dx: Float(dx * scale * Self.speed), dy: Float(dy * scale * Self.speed))
                },
                onScroll: { dx, dy in
                    model.pcInput?.scroll(dx: Float(-dx / Self.pointsPerNotch), dy: Float(-dy / Self.pointsPerNotch))
                },
                onTap: { send(.click()) },
                onTwoFingerTap: { send(.click(.right)) },
                onDragBegan: { send(.click(.left, .press)) },
                onDragEnded: { send(.click(.left, .release)) }
            )
            .background(.card, in: .rect(cornerRadius: 26, style: .continuous))
            .overlay {
                Text("touchpad.hint")
                    .font(.footnote)
                    .foregroundStyle(.textTertiary)
                    .multilineTextAlignment(.center)
                    .padding(32)
                    .allowsHitTesting(false)
            }
            .frame(maxHeight: .infinity)

            HStack(spacing: 8) {
                RemoteButton(title: "touchpad.left") { send(.click()) }
                RemoteButton(title: "touchpad.right") { send(.click(.right)) }
            }

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    KeyButton(label: "key.esc") { send(.key(.escape)) }
                    KeyButton(label: "key.tab") { send(.key(.tab)) }
                    KeyButton(systemImage: "arrow.left", label: "key.left") { send(.key(.left)) }
                    KeyButton(systemImage: "arrow.up", label: "key.up") { send(.key(.up)) }
                    KeyButton(systemImage: "arrow.down", label: "key.down") { send(.key(.down)) }
                    KeyButton(systemImage: "arrow.right", label: "key.right") { send(.key(.right)) }
                    KeyButton(systemImage: "delete.left", label: "key.backspace") { send(.key(.backspace)) }
                    KeyButton(label: "key.enter") { send(.key(.enter)) }
                    KeyButton(label: "key.windows") { send(.key(.windows)) }
                }
                .padding(.horizontal, 2)
            }

            TextField("touchpad.type", text: $typed)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.return)
                .focused($isTyping)
                .padding(.horizontal, 16)
                .frame(minHeight: 48)
                .fenpoCard(cornerRadius: 24)
                .onChange(of: typed) { old, new in
                    typedChanged(from: old, to: new)
                }
                .onSubmit {
                    send(.key(.enter))
                    reset()
                    isTyping = true
                }
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 12)
        .fenpoScreenBackground()
        .navigationTitle("remote.touchpad")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            model.pcInput?.begin()
        }
        .onDisappear {
            model.pcInput?.end()
        }
    }

    private func send(_ input: Fenpo_V1_PcInput) {
        model.pcInput?.send(input)
    }

    private func typedChanged(from old: String, to new: String) {
        if isResetting {
            isResetting = false
            return
        }
        for input in TypedTextDiff.inputs(from: old, to: new) {
            send(input)
        }
        if new.count > Self.keptCharacters {
            reset()
        }
    }

    private func reset() {
        guard !typed.isEmpty else {
            return
        }
        isResetting = true
        typed = ""
    }
}

/// The touch area. UIKit recognizers, since SwiftUI gestures cannot tell one finger from two.
private struct TouchpadSurface: UIViewRepresentable {
    /// Move in points, with the screen scale to turn them into pixels.
    let onMove: (CGFloat, CGFloat, CGFloat) -> Void
    let onScroll: (CGFloat, CGFloat) -> Void
    let onTap: () -> Void
    let onTwoFingerTap: () -> Void
    let onDragBegan: () -> Void
    let onDragEnded: () -> Void

    func makeCoordinator() -> Coordinator {
        Coordinator()
    }

    func makeUIView(context: Context) -> UIView {
        let view = UIView()
        view.backgroundColor = .clear
        view.isMultipleTouchEnabled = true
        let coordinator = context.coordinator

        let move = UIPanGestureRecognizer(target: coordinator, action: #selector(Coordinator.move(_:)))
        move.maximumNumberOfTouches = 1
        let scroll = UIPanGestureRecognizer(target: coordinator, action: #selector(Coordinator.scroll(_:)))
        scroll.minimumNumberOfTouches = 2
        scroll.maximumNumberOfTouches = 2
        let tap = UITapGestureRecognizer(target: coordinator, action: #selector(Coordinator.tap(_:)))
        let twoFingerTap = UITapGestureRecognizer(target: coordinator, action: #selector(Coordinator.twoFingerTap(_:)))
        twoFingerTap.numberOfTouchesRequired = 2
        let hold = UILongPressGestureRecognizer(target: coordinator, action: #selector(Coordinator.hold(_:)))
        hold.minimumPressDuration = 0.45
        hold.allowableMovement = 10

        for recognizer in [move, scroll, tap, twoFingerTap, hold] as [UIGestureRecognizer] {
            recognizer.delegate = coordinator
            view.addGestureRecognizer(recognizer)
        }
        return view
    }

    func updateUIView(_ view: UIView, context: Context) {
        context.coordinator.surface = self
    }

    @MainActor
    final class Coordinator: NSObject, UIGestureRecognizerDelegate {
        var surface: TouchpadSurface?
        private var isDragging = false
        private var lastHoldLocation: CGPoint = .zero

        @objc func move(_ recognizer: UIPanGestureRecognizer) {
            guard !isDragging, let view = recognizer.view else {
                return
            }
            let translation = recognizer.translation(in: view)
            recognizer.setTranslation(.zero, in: view)
            surface?.onMove(translation.x, translation.y, view.traitCollection.displayScale)
        }

        @objc func scroll(_ recognizer: UIPanGestureRecognizer) {
            guard let view = recognizer.view else {
                return
            }
            let translation = recognizer.translation(in: view)
            recognizer.setTranslation(.zero, in: view)
            surface?.onScroll(translation.x, translation.y)
        }

        @objc func tap(_ recognizer: UITapGestureRecognizer) {
            surface?.onTap()
        }

        @objc func twoFingerTap(_ recognizer: UITapGestureRecognizer) {
            surface?.onTwoFingerTap()
        }

        @objc func hold(_ recognizer: UILongPressGestureRecognizer) {
            guard let view = recognizer.view else {
                return
            }
            let location = recognizer.location(in: view)
            switch recognizer.state {
            case .began:
                isDragging = true
                lastHoldLocation = location
                UIImpactFeedbackGenerator(style: .medium).impactOccurred()
                surface?.onDragBegan()
            case .changed:
                let dx = location.x - lastHoldLocation.x
                let dy = location.y - lastHoldLocation.y
                lastHoldLocation = location
                surface?.onMove(dx, dy, view.traitCollection.displayScale)
            case .ended, .cancelled, .failed:
                if isDragging {
                    isDragging = false
                    surface?.onDragEnded()
                }
            default:
                break
            }
        }

        func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer) -> Bool {
            true
        }
    }
}

/// A capsule button of the remote screens.
struct RemoteButton: View {
    let title: LocalizedStringKey
    var systemImage: String?
    var isProminent = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Group {
                if let systemImage {
                    Label(title, systemImage: systemImage)
                } else {
                    Text(title)
                }
            }
            .font(.headline)
            .foregroundStyle(isProminent ? Color.onAccent : Color.textPrimary)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .frame(minHeight: 52)
            .background(isProminent ? Color.bloom : Color.card, in: .rect(cornerRadius: 22, style: .continuous))
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
    }
}

/// A small key of the special key row.
private struct KeyButton: View {
    var systemImage: String?
    let label: LocalizedStringKey
    let action: () -> Void

    init(systemImage: String? = nil, label: LocalizedStringKey, action: @escaping () -> Void) {
        self.systemImage = systemImage
        self.label = label
        self.action = action
    }

    var body: some View {
        Button(action: action) {
            Group {
                if let systemImage {
                    Image(systemName: systemImage)
                        .accessibilityLabel(Text(label))
                } else {
                    Text(label)
                }
            }
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.textPrimary)
            .padding(.horizontal, 14)
            .frame(minWidth: 48, minHeight: 40)
            .fenpoCard(cornerRadius: 14)
        }
        .buttonStyle(.plain)
    }
}

/// Why the remote does nothing right now, with what to do.
struct RemoteProblemBanner: View {
    let message: LocalizedStringResource

    var body: some View {
        Label {
            Text(message)
        } icon: {
            Image(systemName: "exclamationmark.circle")
                .foregroundStyle(.accent)
        }
        .font(.footnote)
        .foregroundStyle(.textPrimary)
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.bloom.opacity(0.1), in: .rect(cornerRadius: 18, style: .continuous))
    }
}

#Preview("Light · Türkçe") {
    NavigationStack {
        TouchpadView()
    }
    .sampleModel()
    .preview(.light, "tr")
}

#Preview("Dark · English") {
    NavigationStack {
        TouchpadView()
    }
    .sampleModel()
    .preview(.dark, "en")
}

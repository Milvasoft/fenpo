import SwiftUI
import UIKit

extension View {
    /// Turns off the navigation controller's swipe-back gestures while this screen shows, for screens
    /// whose own surface takes horizontal drags (the touchpad, the laser pad). The back button stays.
    func disablesBackSwipe() -> some View {
        background(BackSwipeBlocker())
    }
}

/// Finds the navigation controller above it and disables its pan gestures (the edge swipe and, on
/// iOS 26, the swipe anywhere in the content) until it leaves the window.
private struct BackSwipeBlocker: UIViewRepresentable {
    func makeUIView(context: Context) -> BlockerView {
        BlockerView()
    }

    func updateUIView(_ view: BlockerView, context: Context) {
    }

    final class BlockerView: UIView {
        private var disabled: [UIGestureRecognizer] = []

        override func didMoveToWindow() {
            super.didMoveToWindow()
            if window != nil {
                block()
            } else {
                restore()
            }
        }

        private func block() {
            guard disabled.isEmpty, let navigation = navigationController() else {
                return
            }
            var recognizers = navigation.view.gestureRecognizers ?? []
            if let edge = navigation.interactivePopGestureRecognizer {
                recognizers.append(edge)
            }
            for recognizer in recognizers where recognizer is UIPanGestureRecognizer && recognizer.isEnabled {
                recognizer.isEnabled = false
                disabled.append(recognizer)
            }
        }

        private func restore() {
            for recognizer in disabled {
                recognizer.isEnabled = true
            }
            disabled = []
        }

        private func navigationController() -> UINavigationController? {
            var responder: UIResponder? = self
            while let next = responder?.next {
                if let navigation = next as? UINavigationController {
                    return navigation
                }
                if let controller = next as? UIViewController, let navigation = controller.navigationController {
                    return navigation
                }
                responder = next
            }
            return nil
        }
    }
}

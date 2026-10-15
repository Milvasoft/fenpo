import FenpoApple
import SwiftUI
import UIKit

/// Entry point of the share extension. Hosts the SwiftUI share view.
final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        overrideUserInterfaceStyle = AppearanceStore().preference.userInterfaceStyle

        let host = UIHostingController(rootView: ShareView { [weak self] in
            self?.extensionContext?.completeRequest(returningItems: nil, completionHandler: nil)
        })
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)
    }
}

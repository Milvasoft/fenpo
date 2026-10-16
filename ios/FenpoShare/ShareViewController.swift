import FenpoApple
import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Entry point of the share extension. Hosts the SwiftUI share view.
final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        overrideUserInterfaceStyle = AppearanceStore().preference.userInterfaceStyle

        let model = ShareModel(items: extensionContext?.inputItems as? [NSExtensionItem] ?? [])
        let host = UIHostingController(rootView: ShareView(model: model) { [weak self] in
            self?.extensionContext?.completeRequest(returningItems: nil, completionHandler: nil)
        })
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)
    }
}

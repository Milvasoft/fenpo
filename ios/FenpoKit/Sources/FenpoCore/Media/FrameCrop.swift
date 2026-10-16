/// Where to cut a camera frame so it fills a landscape webcam picture: the largest centered
/// rectangle with the output's aspect ratio. A portrait frame loses its top and bottom, the way a
/// webcam shows a person standing in front of it.
public enum FrameCrop {
    /// A rectangle in pixels, origin at a corner.
    public struct Rect: Equatable, Sendable {
        public var x: Double
        public var y: Double
        public var width: Double
        public var height: Double

        /// Creates a rectangle.
        public init(x: Double, y: Double, width: Double, height: Double) {
            self.x = x
            self.y = y
            self.width = width
            self.height = height
        }
    }

    /// The centered crop of a `width` x `height` frame with the aspect ratio of `targetWidth` x
    /// `targetHeight`.
    /// - Parameters:
    ///   - width: Source width.
    ///   - height: Source height.
    ///   - targetWidth: Output width.
    ///   - targetHeight: Output height.
    public static func centered(width: Int, height: Int, targetWidth: Int, targetHeight: Int) -> Rect {
        let sourceWidth = Double(width)
        let sourceHeight = Double(height)
        guard sourceWidth > 0, sourceHeight > 0, targetWidth > 0, targetHeight > 0 else {
            return Rect(x: 0, y: 0, width: sourceWidth, height: sourceHeight)
        }
        let aspect = Double(targetWidth) / Double(targetHeight)
        if sourceWidth / sourceHeight < aspect {
            let cropHeight = sourceWidth / aspect
            return Rect(x: 0, y: (sourceHeight - cropHeight) / 2, width: sourceWidth, height: cropHeight)
        }
        let cropWidth = sourceHeight * aspect
        return Rect(x: (sourceWidth - cropWidth) / 2, y: 0, width: cropWidth, height: sourceHeight)
    }
}

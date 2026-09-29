import Foundation
import CoreGraphics

public enum BeaconGeometry {
    public static func clamp(_ frame: CGRect, to area: CGRect) -> CGRect {
        CGRect(x: max(area.minX, min(frame.minX, area.maxX - frame.width)),
               y: max(area.minY, min(frame.minY, area.maxY - frame.height)),
               width: frame.width, height: frame.height)
    }

    /// AX uses the main display's top-left; AppKit uses its bottom-left.
    public static func appKitRect(fromAX rect: CGRect, mainDisplayHeight: Double) -> CGRect {
        CGRect(x: rect.minX, y: mainDisplayHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    public static func bubble(caret: CGRect, size: CGSize, visibleFrame: CGRect) -> CGRect {
        var x = caret.maxX + 4
        var y = caret.maxY + 4
        if x + size.width > visibleFrame.maxX { x = caret.minX - size.width - 4 }
        if y + size.height > visibleFrame.maxY { y = caret.minY - size.height - 4 }
        return clamp(CGRect(origin: CGPoint(x: x, y: y), size: size), to: visibleFrame)
    }
}

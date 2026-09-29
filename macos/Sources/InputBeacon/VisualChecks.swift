import AppKit
import BeaconCore

/// Uses the production painter; suitable for inspecting CI rendering without AX access.
enum VisualChecks {
    static func render(to path: String) throws {
        _ = NSApplication.shared
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 1000, pixelsHigh: 560,
                                     bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                                     isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
        NSColor.white.setFill(); CGRect(x: 0, y: 0, width: 1000, height: 560).fill()
        NSColor(srgbRed: 0.08, green: 0.11, blue: 0.16, alpha: 1).setFill()
        CGRect(x: 500, y: 0, width: 500, height: 560).fill()
        let samples = [InputState(mode: .chinese), InputState(mode: .english, caps: true),
                       InputState(mode: .english, caps: true, shift: true), InputState(mode: .unknown)]
        for column in 0...1 {
            for (row, state) in samples.enumerated() {
                for isBubble in [false, true] {
                    NSGraphicsContext.saveGraphicsState()
                    let transform = NSAffineTransform()
                    transform.translateX(by: CGFloat(column * 500 + (isBubble ? 270 : 50)), yBy: CGFloat(430 - row * 125))
                    transform.concat()
                    let view = StatusView(frame: CGRect(x: 0, y: 0, width: 168, height: 72))
                    view.state = state; view.bubble = isBubble; view.draw(view.bounds)
                    NSGraphicsContext.restoreGraphicsState()
                }
            }
        }
        NSGraphicsContext.restoreGraphicsState()
        try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
        print("Rendered production overlay preview: \(path)")
    }
}

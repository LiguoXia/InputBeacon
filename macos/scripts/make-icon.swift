import AppKit

let directory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = size * scale
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
                                      bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                                      isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
        let transform = NSAffineTransform(); transform.scale(by: CGFloat(pixels) / 512); transform.concat()
        NSColor(srgbRed: 0.04, green: 0.17, blue: 0.31, alpha: 1).setFill()
        NSBezierPath(roundedRect: CGRect(x: 32, y: 32, width: 448, height: 448), xRadius: 104, yRadius: 104).fill()
        NSColor(srgbRed: 0.24, green: 0.70, blue: 0.98, alpha: 1).setFill()
        NSBezierPath(roundedRect: CGRect(x: 74, y: 111, width: 364, height: 280), xRadius: 52, yRadius: 52).fill()
        let text = "中A" as NSString
        let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 148, weight: .semibold), .foregroundColor: NSColor.white]
        let dimensions = text.size(withAttributes: attributes)
        text.draw(at: CGPoint(x: (512 - dimensions.width) / 2, y: (512 - dimensions.height) / 2 + 3), withAttributes: attributes)
        NSColor.white.setFill()
        NSBezierPath(ovalIn: CGRect(x: 324, y: 139, width: 18, height: 18)).fill()
        NSGraphicsContext.restoreGraphicsState()
        let suffix = scale == 2 ? "@2x" : ""
        try bitmap.representation(using: .png, properties: [:])!.write(to: directory.appendingPathComponent("icon_\(size)x\(size)\(suffix).png"))
    }
}

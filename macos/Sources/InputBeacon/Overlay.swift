import AppKit
import BeaconCore

final class BeaconPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class StatusView: NSView {
    var state = InputState() { didSet { if oldValue != state { needsDisplay = true } } }
    var customColor: NSColor? { didSet { needsDisplay = true } }
    var bubble = false
    var onMoved: ((CGPoint) -> Void)?
    var contextMenu: (() -> NSMenu)?
    override var mouseDownCanMoveWindow: Bool { true }
    override func mouseDown(with event: NSEvent) {
        window?.performDrag(with: event)
        if let origin = window?.frame.origin { onMoved?(origin) }
    }
    override func rightMouseDown(with event: NSEvent) {
        if let menu = contextMenu?() { NSMenu.popUpContextMenu(menu, with: event, for: self) }
    }
    override func draw(_ dirtyRect: NSRect) {
        if bubble {
            NSColor.windowBackgroundColor.withAlphaComponent(0.94).setFill()
            NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 9, yRadius: 9).fill()
            NSColor.separatorColor.setStroke()
            NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 9, yRadius: 9).stroke()
        }
        let scale = bounds.height / 36
        let font = NSFont.systemFont(ofSize: 21 * scale, weight: .semibold)
        let modeColor = customColor ?? NSColor(srgbRed: 0, green: 0.44, blue: 0.89, alpha: 1)
        let caseColor = customColor ?? (state.uppercase ? NSColor.systemOrange : modeColor)
        let text = NSMutableAttributedString(string: state.mode.symbol + " ", attributes: [.font: font, .foregroundColor: modeColor])
        text.append(NSAttributedString(string: state.uppercase ? "A" : "a", attributes: [.font: font, .foregroundColor: caseColor]))
        let size = text.size()
        let origin = CGPoint(x: (bounds.width - size.width) / 2, y: (bounds.height - size.height) / 2 + 1)
        text.draw(at: origin)
        if state.caps {
            caseColor.setFill()
            NSBezierPath(ovalIn: CGRect(x: origin.x + size.width - 9 * scale, y: 3 * scale, width: 3 * scale, height: 3 * scale)).fill()
        }
        if state.shift {
            ("↑" as NSString).draw(at: CGPoint(x: bounds.width - 12 * scale, y: 9 * scale),
                                   withAttributes: [.font: NSFont.systemFont(ofSize: 12 * scale), .foregroundColor: caseColor])
        }
    }
}

final class Overlay {
    let panel: BeaconPanel
    let view = StatusView()
    init(bubble: Bool) {
        panel = BeaconPanel(contentRect: CGRect(x: 0, y: 0, width: 84, height: 36),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isFloatingPanel = true; panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        panel.isOpaque = false; panel.backgroundColor = .clear
        panel.hasShadow = bubble; panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false; panel.ignoresMouseEvents = bubble
        view.bubble = bubble; view.autoresizingMask = [.width, .height]
        panel.contentView = view
    }
    func update(state: InputState, preferences: Preferences) {
        view.state = state
        let color = preferences.colorHex.flatMap(Preferences.color)
        if view.customColor != color { view.customColor = color }
        panel.alphaValue = preferences.opacity
        view.toolTip = "\(state.sourceName)；Caps \(state.caps ? "开" : "关")，Shift \(state.shift ? "按下" : "松开")。中/英表示系统输入源。"
    }
}

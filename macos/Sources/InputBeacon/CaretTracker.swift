import AppKit
import ApplicationServices

struct CaretSample {
    let pid: pid_t
    let field: UInt64
    let rect: CGRect
    let timestamp: Double
}

/// Serialized AX requests never block the UI. Only one request may be in flight.
final class CaretTracker {
    private let queue = DispatchQueue(label: "InputBeacon.accessibility", qos: .utility)
    private var busy = false // main queue only
    private var previousElement: AXUIElement? // worker queue only
    private var previousPID: pid_t = 0
    private var identity: UInt64 = 0
    private(set) var sample: CaretSample?

    func refresh(pid: pid_t) {
        guard !busy else { return }
        guard AXIsProcessTrusted(), pid != ProcessInfo.processInfo.processIdentifier else { sample = nil; return }
        busy = true
        queue.async { [self] in
            let result = read(pid: pid)
            DispatchQueue.main.async { [self] in sample = result; busy = false }
        }
    }

    private func read(pid: pid_t) -> CaretSample? {
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 0.08)
        var raw: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedUIElementAttribute as CFString, &raw) == .success,
              let raw, CFGetTypeID(raw) == AXUIElementGetTypeID() else { return nil }
        let element = unsafeBitCast(raw, to: AXUIElement.self)
        AXUIElementSetMessagingTimeout(element, 0.08)
        var selected: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &selected) == .success,
              let selected, CFGetTypeID(selected) == AXValueGetTypeID() else { return nil }
        let selectedValue = unsafeBitCast(selected, to: AXValue.self)
        var range = CFRange()
        guard AXValueGetType(selectedValue) == .cfRange,
              AXValueGetValue(selectedValue, .cfRange, &range), range.location >= 0,
              range.length == 0 else { return nil } // selection is not an insertion caret
        var rawBounds: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute as CFString,
                                                          selectedValue, &rawBounds) == .success,
              let rawBounds, CFGetTypeID(rawBounds) == AXValueGetTypeID() else { return nil }
        let value = unsafeBitCast(rawBounds, to: AXValue.self)
        var rect = CGRect.zero
        guard AXValueGetType(value) == .cgRect, AXValueGetValue(value, .cgRect, &rect),
              rect.origin.x.isFinite, rect.origin.y.isFinite, rect.width.isFinite, rect.height.isFinite,
              rect.height > 0, rect.height < 200, rect.width >= 0, rect.width <= 8 else { return nil }
        // Recheck focus after the multi-call query; never publish another field's caret.
        var current: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedUIElementAttribute as CFString, &current) == .success,
              let current, CFEqual(element, current) else { return nil }
        if previousPID != pid || previousElement == nil || !CFEqual(previousElement!, element) {
            identity &+= 1; previousPID = pid; previousElement = element
        }
        return CaretSample(pid: pid, field: identity, rect: rect, timestamp: ProcessInfo.processInfo.systemUptime)
    }
}

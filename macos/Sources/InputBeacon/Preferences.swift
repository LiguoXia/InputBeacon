import AppKit

final class Preferences {
    private let defaults = UserDefaults.standard
    init() {
        defaults.register(defaults: ["showFloating": true, "showMenuStatus": false,
                                    "followCaret": false, "followSeconds": 3,
                                    "scale": 100, "opacity": 94, "clickThrough": false])
    }
    var showFloating: Bool { get { defaults.bool(forKey: "showFloating") } set { defaults.set(newValue, forKey: "showFloating") } }
    var showMenuStatus: Bool { get { defaults.bool(forKey: "showMenuStatus") } set { defaults.set(newValue, forKey: "showMenuStatus") } }
    var followCaret: Bool { get { defaults.bool(forKey: "followCaret") } set { defaults.set(newValue, forKey: "followCaret") } }
    var clickThrough: Bool { get { defaults.bool(forKey: "clickThrough") } set { defaults.set(newValue, forKey: "clickThrough") } }
    var followSeconds: Int { get { max(0, min(60, defaults.integer(forKey: "followSeconds"))) } set { defaults.set(newValue, forKey: "followSeconds") } }
    var scale: CGFloat { get { CGFloat(max(80, min(150, defaults.integer(forKey: "scale")))) / 100 } set { defaults.set(Int(newValue * 100), forKey: "scale") } }
    var opacity: CGFloat { get { CGFloat(max(45, min(100, defaults.integer(forKey: "opacity")))) / 100 } set { defaults.set(Int(newValue * 100), forKey: "opacity") } }
    var colorHex: String? { get { defaults.string(forKey: "color") } set { defaults.set(newValue, forKey: "color") } }
    var origin: CGPoint? {
        get {
            guard defaults.object(forKey: "x") != nil else { return nil }
            return CGPoint(x: defaults.double(forKey: "x"), y: defaults.double(forKey: "y"))
        }
        set {
            if let newValue { defaults.set(newValue.x, forKey: "x"); defaults.set(newValue.y, forKey: "y") }
            else { defaults.removeObject(forKey: "x"); defaults.removeObject(forKey: "y") }
        }
    }
    static func color(_ hex: String) -> NSColor? {
        var text = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.hasPrefix("#") { text.removeFirst() }
        if text.count == 3 { text = text.map { "\($0)\($0)" }.joined() }
        guard text.count == 6, text.allSatisfy({ $0.isHexDigit }), let value = UInt32(text, radix: 16) else { return nil }
        return NSColor(srgbRed: CGFloat((value >> 16) & 255) / 255,
                       green: CGFloat((value >> 8) & 255) / 255, blue: CGFloat(value & 255) / 255, alpha: 1)
    }
}

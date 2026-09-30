import Foundation
import CoreFoundation

public enum ThirdPartyInputProvider: String, Equatable {
    case sogou, squirrel
    public static func identify(sourceID: String, bundleID: String = "") -> Self? {
        let ids = [sourceID.lowercased(), bundleID.lowercased()]
        if ids.contains(where: { $0 == "com.sogou.inputmethod.sogou" || $0.hasPrefix("com.sogou.inputmethod.sogou.") }) { return .sogou }
        if ids.contains(where: { $0 == "im.rime.inputmethod.squirrel" || $0.hasPrefix("im.rime.inputmethod.squirrel.") }) { return .squirrel }
        return nil
    }
    public var requestName: String {
        switch self {
        case .sogou: return "SGFetchSelectPreviousIMEHotKeyAndSogouInfo"
        case .squirrel: return "SquirrelGetASCIIModeNotification"
        }
    }
    public var responseName: String {
        switch self {
        case .sogou: return "SGSystemPreviousIMEHotKeyAndSogouInfo"
        case .squirrel: return "SquirrelASCIIModeResponse"
        }
    }
    /// Sogou currentShowStatus wire values, NOT the different currentInputStatus enum.
    /// 0: Chinese; 1: English; 2: automatic/address-bar English; 3: Caps English.
    public func decode(object: Any?, userInfo: [AnyHashable: Any]?) -> InputMode? {
        switch self {
        case .sogou:
            guard let value = userInfo?["inputStatus"] else { return nil }
            let number: Int?
            if let text = value as? String {
                number = ["0": 0, "1": 1, "2": 2, "3": 3][text]
            } else if let value = value as? NSNumber,
                      CFGetTypeID(value) != CFBooleanGetTypeID(),
                      (0...3).contains(value.doubleValue), value.doubleValue.rounded() == value.doubleValue {
                number = value.intValue
            } else { number = nil }
            switch number {
            case 0: return .chinese
            case 1, 2, 3: return .english
            default: return nil
            }
        case .squirrel:
            switch object as? String {
            case "ascii": return .english
            case "nascii": return .chinese
            default: return nil
            }
        }
    }
}

public struct InputQueryContext: Equatable {
    public let provider: ThirdPartyInputProvider
    public let sourceID: String
    public let applicationPID: Int32
    public init(provider: ThirdPartyInputProvider, sourceID: String, applicationPID: Int32) {
        self.provider = provider; self.sourceID = sourceID; self.applicationPID = applicationPID
    }
}

/// One outstanding request, bounded freshness, no optimistic Shift toggling.
/// Vendor replies have no request IDs. Consume old context replies first and use
/// a quiet interval after timeouts to reduce ambiguity from delayed delivery.
public struct InputModeQuery {
    private struct Pending { let context: InputQueryContext; let sent: Double }
    private struct Sample { let context: InputQueryContext; let mode: InputMode; let received: Double }
    private var context: InputQueryContext?
    private var pending: Pending?
    private var sample: Sample?
    private var nextRequest = 0.0
    public private(set) var diagnostic = "inactive"
    public init() {}
    public mutating func update(context: InputQueryContext?) {
        guard self.context != context else { return }
        self.context = context; sample = nil; nextRequest = 0
        diagnostic = context == nil ? "inactive" : "waiting"
        // Retain the pending request to consume/discard its late reply first.
    }
    public mutating func request(now: Double) -> InputQueryContext? {
        if let pending, now - pending.sent >= 0.5 || now < pending.sent {
            self.pending = nil; sample = nil; nextRequest = now + 0.25; diagnostic = "timeout"
        }
        guard let context, pending == nil, now >= nextRequest else { return nil }
        pending = Pending(context: context, sent: now); nextRequest = now + 0.2
        return context
    }
    @discardableResult
    public mutating func receive(provider: ThirdPartyInputProvider, mode: InputMode?, now: Double) -> Bool {
        guard let pending, pending.context.provider == provider else { return false }
        self.pending = nil
        guard pending.context == context, now >= pending.sent, now - pending.sent < 0.5 else {
            diagnostic = "discarded-stale-reply"; return false
        }
        guard let mode, mode == .chinese || mode == .english else {
            sample = nil; diagnostic = "unsupported-reply"; return false
        }
        sample = Sample(context: pending.context, mode: mode, received: now)
        diagnostic = "confirmed"; return true
    }
    public func mode(now: Double) -> InputMode? {
        guard let sample, sample.context == context, now >= sample.received,
              now - sample.received < 0.75 else { return nil }
        return sample.mode
    }
}

public enum InputSourceClassifier {
    /// ASCII capability is not the live mode of a Chinese input method.
    public static func classify(sourceID: String, bundleID: String, languages: [String]) -> InputMode {
        if ThirdPartyInputProvider.identify(sourceID: sourceID, bundleID: bundleID) != nil { return .chinese }
        let id = sourceID.lowercased()
        if ["com.apple.keylayout.abc", "com.apple.keylayout.us", "com.apple.keylayout.british"].contains(id) { return .english }
        if id.hasPrefix("com.apple.inputmethod.scim.") || id.hasPrefix("com.apple.inputmethod.tcim.") { return .chinese }
        return languages.map { InputState.classify(language: $0) }.first(where: { $0 != .unknown }) ?? .unknown
    }
}

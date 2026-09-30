import Foundation

public enum InputMode: String, Equatable {
    case unknown, english, chinese, other
    public var symbol: String {
        switch self {
        case .unknown: return "?"
        case .english: return "英"
        case .chinese: return "中"
        case .other: return "语"
        }
    }
}

public struct InputState: Equatable {
    public var mode: InputMode
    public var sourceID: String
    public var sourceName: String
    public var caps: Bool
    public var shift: Bool
    public var modeOrigin = "system-input-source"
    public var modeDetail: String {
        switch modeOrigin {
        case "sogou-status": return "搜狗当前中英文状态"
        case "squirrel-status": return "鼠须管当前中英文状态"
        case "sogou-waiting": return "等待搜狗状态回复；可复制状态诊断排查"
        case "squirrel-waiting": return "等待鼠须管状态回复；旧版本可能不支持状态查询"
        default: return "系统输入源语言（非输入法内部状态）"
        }
    }
    public var uppercase: Bool { caps != shift }
    public var modeKey: String? { mode == .unknown ? nil : "\(mode.rawValue):\(sourceID)" }
    public var label: String { "\(mode.symbol)  \(uppercase ? "A" : "a")\(caps ? "•" : "")\(shift ? "↑" : "")" }

    public init(mode: InputMode = .unknown, sourceID: String = "", sourceName: String = "未知输入源", caps: Bool = false, shift: Bool = false) {
        self.mode = mode; self.sourceID = sourceID; self.sourceName = sourceName
        self.caps = caps; self.shift = shift
    }

    public static func classify(language: String?) -> InputMode {
        guard let language, !language.isEmpty else { return .unknown }
        let primary = language.lowercased().split(whereSeparator: { $0 == "-" || $0 == "_" }).first
        if primary == "zh" { return .chinese }
        if primary == "en" { return .english }
        return .other
    }
}

import AppKit
import Carbon
import BeaconCore

final class InputProbe {
    func read() -> InputState {
        let flags = CGEventSource.flagsState(.combinedSessionState)
        var state = InputState(caps: flags.contains(.maskAlphaShift), shift: flags.contains(.maskShift))
        guard let unmanaged = TISCopyCurrentKeyboardInputSource() else { return state }
        let source = unmanaged.takeRetainedValue()
        func property<T>(_ key: CFString, as: T.Type) -> T? {
            guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
            return Unmanaged<AnyObject>.fromOpaque(pointer).takeUnretainedValue() as? T
        }
        state.sourceID = property(kTISPropertyInputSourceID, as: String.self) ?? ""
        state.sourceName = property(kTISPropertyLocalizedName, as: String.self) ?? "未知输入源"
        let languages = property(kTISPropertyInputSourceLanguages, as: [String].self)
        state.mode = InputState.classify(language: languages?.first)
        return state
    }
}

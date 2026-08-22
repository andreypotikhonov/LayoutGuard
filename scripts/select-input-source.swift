import Carbon.HIToolbox
import Foundation

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: swift select-input-source.swift <english|russian>")
}

let requested = CommandLine.arguments[1].lowercased()
let wantedIdentifier: String
switch requested {
case "english": wantedIdentifier = "com.apple.keylayout.ABC"
case "russian": wantedIdentifier = "com.apple.keylayout.Russian"
default: fatalError("Unsupported language: \(requested)")
}

func stringProperty(_ key: CFString, source: TISInputSource) -> String? {
    guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
    return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
}

let filter = [
    kTISPropertyInputSourceCategory as String: kTISCategoryKeyboardInputSource!,
    kTISPropertyInputSourceIsSelectCapable as String: true
] as CFDictionary

guard let list = TISCreateInputSourceList(filter, false)?.takeRetainedValue() as NSArray? else {
    fatalError("Unable to list input sources")
}

guard let source = list.compactMap({ $0 as? TISInputSource }).first(where: {
    stringProperty(kTISPropertyInputSourceID, source: $0) == wantedIdentifier
}) else {
    fatalError("Input source not installed: \(wantedIdentifier)")
}

guard TISSelectInputSource(source) == noErr else {
    fatalError("Unable to select input source: \(wantedIdentifier)")
}

print(wantedIdentifier)

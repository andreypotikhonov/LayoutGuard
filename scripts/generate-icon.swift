import AppKit
import Foundation

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: swift generate-icon.swift <output.png>")
}

let outputURL = URL(fileURLWithPath: CommandLine.arguments[1])
let width = 1024
let height = 1024
let size = NSSize(width: width, height: height)
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: width,
    pixelsHigh: height,
    bitsPerSample: 8,
    samplesPerPixel: 4,
    hasAlpha: true,
    isPlanar: false,
    colorSpaceName: .deviceRGB,
    bytesPerRow: 0,
    bitsPerPixel: 0
), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
    fatalError("Unable to create bitmap context")
}

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context
defer { NSGraphicsContext.restoreGraphicsState() }

let bounds = NSRect(origin: .zero, size: size)
NSColor.clear.setFill()
bounds.fill()

let tile = NSBezierPath(roundedRect: bounds.insetBy(dx: 70, dy: 70), xRadius: 220, yRadius: 220)
let gradient = NSGradient(colors: [
    NSColor(calibratedRed: 0.16, green: 0.34, blue: 0.96, alpha: 1),
    NSColor(calibratedRed: 0.48, green: 0.20, blue: 0.92, alpha: 1)
])!
gradient.draw(in: tile, angle: -45)

let keyRect = NSRect(x: 184, y: 236, width: 656, height: 552)
let key = NSBezierPath(roundedRect: keyRect, xRadius: 116, yRadius: 116)
NSColor(calibratedWhite: 1, alpha: 0.19).setFill()
key.fill()
NSColor(calibratedWhite: 1, alpha: 0.36).setStroke()
key.lineWidth = 7
key.stroke()

let paragraph = NSMutableParagraphStyle()
paragraph.alignment = .center

func draw(_ text: String, in rect: NSRect, size: CGFloat, alpha: CGFloat = 1) {
    let attributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: size, weight: .bold),
        .foregroundColor: NSColor(calibratedWhite: 1, alpha: alpha),
        .paragraphStyle: paragraph
    ]
    text.draw(in: rect, withAttributes: attributes)
}

draw("A", in: NSRect(x: 220, y: 400, width: 260, height: 300), size: 238)
draw("Я", in: NSRect(x: 542, y: 292, width: 260, height: 300), size: 238)
draw("↔", in: NSRect(x: 380, y: 330, width: 264, height: 190), size: 132, alpha: 0.9)

context.flushGraphics()

guard let png = bitmap.representation(using: .png, properties: [:]) else {
    fatalError("Unable to render icon")
}

try png.write(to: outputURL, options: .atomic)

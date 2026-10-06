import CoreMIDI
import Foundation

var client = MIDIClientRef()
var inputPort = MIDIPortRef()

let clientStatus = MIDIClientCreateWithBlock("MIDI USB Doctor Diagnostics" as CFString, &client) { _ in }
guard clientStatus == noErr else {
    fatalError("Could not create CoreMIDI client: \(clientStatus)")
}

let portStatus = MIDIInputPortCreateWithBlock(client, "Diagnostic Input" as CFString, &inputPort) {
    packetList, sourcePointer in

    let sourceNumber = sourcePointer.map { Int(bitPattern: $0) } ?? 0
    var packet = packetList.pointee.packet

    for _ in 0..<packetList.pointee.numPackets {
        let bytes = withUnsafeBytes(of: &packet.data) { data in
            Array(data.prefix(Int(packet.length)))
        }
        let formatted = bytes.map { String(format: "%02X", $0) }.joined(separator: " ")
        print("SOURCE \(sourceNumber): \(formatted)")
        packet = MIDIPacketNext(&packet).pointee
    }
}

guard portStatus == noErr else {
    fatalError("Could not create CoreMIDI input port: \(portStatus)")
}

let sourceCount = MIDIGetNumberOfSources()
print("Monitoring \(sourceCount) CoreMIDI input sources for 30 seconds…")

for index in 0..<sourceCount {
    let source = MIDIGetSource(index)
    let sourcePointer = UnsafeMutableRawPointer(bitPattern: index + 1)
    let status = MIDIPortConnectSource(inputPort, source, sourcePointer)
    print("Source \(index + 1): reference \(source), connect status \(status)")
}

RunLoop.current.run(until: Date().addingTimeInterval(30))

MIDIPortDispose(inputPort)
MIDIClientDispose(client)
print("Diagnostic finished.")

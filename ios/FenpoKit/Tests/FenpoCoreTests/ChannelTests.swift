import FenpoCore
import FenpoProtocol
import Testing

struct EnvelopeFramingTests {
    @Test
    func framesCarryABigEndianLength() throws {
        let frame = try EnvelopeFraming.frame([0xAA, 0xBB, 0xCC])
        #expect(frame == [0, 0, 0, 3, 0xAA, 0xBB, 0xCC])
    }

    @Test
    func decoderHandlesAnyChunking() throws {
        let payloads: [[UInt8]] = [[1], [], Array(repeating: 7, count: 300), [9, 9]]
        let stream = try payloads.flatMap { try EnvelopeFraming.frame($0) }

        for chunkSize in [1, 2, 3, 5, 64, stream.count] {
            var decoder = EnvelopeFrameDecoder()
            var received: [[UInt8]] = []
            var offset = 0
            while offset < stream.count {
                let end = min(offset + chunkSize, stream.count)
                received += try decoder.append(stream[offset..<end])
                offset = end
            }
            #expect(received == payloads, "chunk size \(chunkSize)")
            #expect(decoder.bufferedByteCount == 0)
        }
    }

    @Test
    func decoderRejectsOversizedFrames() {
        var decoder = EnvelopeFrameDecoder()
        let tooLong = EnvelopeFraming.maxFrameLength + 1
        let header: [UInt8] = [UInt8(tooLong >> 24), UInt8(tooLong >> 16 & 0xFF), UInt8(tooLong >> 8 & 0xFF), UInt8(tooLong & 0xFF)]
        #expect(throws: EnvelopeFraming.Error.frameTooLarge(tooLong)) {
            try decoder.append(header)
        }
    }

    @Test
    func encoderRejectsOversizedPayloads() {
        #expect(throws: EnvelopeFraming.Error.frameTooLarge(EnvelopeFraming.maxFrameLength + 1)) {
            try EnvelopeFraming.frame([UInt8](repeating: 0, count: EnvelopeFraming.maxFrameLength + 1))
        }
    }
}

struct BLEChunkingTests {
    @Test(arguments: [20, 23, 185, 512])
    func chunksRoundTrip(maximumChunkLength: Int) throws {
        let message = (0..<1_000).map { UInt8(truncatingIfNeeded: $0 * 31) }
        let chunks = try BLEChunking.chunks(of: message, maximumChunkLength: maximumChunkLength)
        #expect(chunks.allSatisfy { $0.count <= maximumChunkLength })
        #expect(chunks.first?.first == BLEChunking.Flags.first.rawValue)
        #expect(chunks.last.map { BLEChunking.Flags(rawValue: $0[0]).contains(.last) } == true)

        var reassembler = BLEReassembler()
        var result: [UInt8]?
        for chunk in chunks {
            #expect(result == nil)
            result = try reassembler.append(chunk)
        }
        #expect(result == message)
    }

    @Test
    func smallMessageIsOneChunkWithBothFlags() throws {
        let chunks = try BLEChunking.chunks(of: [1, 2, 3], maximumChunkLength: 20)
        #expect(chunks == [[0x03, 0x00, 0x00, 1, 2, 3]])
    }

    @Test
    func sequenceIsBigEndian() throws {
        let chunks = try BLEChunking.chunks(of: [UInt8](repeating: 0, count: 300), maximumChunkLength: 4)
        #expect(chunks.count == 300)
        #expect(Array(chunks[256][1...2]) == [0x01, 0x00])
    }

    @Test
    func rejectsLargeMessages() {
        #expect(throws: BLEChunking.Error.messageTooLarge(BLEChunking.maxMessageLength + 1)) {
            try BLEChunking.chunks(of: [UInt8](repeating: 0, count: BLEChunking.maxMessageLength + 1), maximumChunkLength: 185)
        }
    }

    @Test
    func reassemblerDropsOutOfOrderMessages() throws {
        let chunks = try BLEChunking.chunks(of: [UInt8](repeating: 5, count: 100), maximumChunkLength: 20)
        var reassembler = BLEReassembler()
        _ = try reassembler.append(chunks[0])
        #expect(throws: BLEChunking.Error.unexpectedSequence(expected: 1, received: 2)) {
            try reassembler.append(chunks[2])
        }
        #expect(throws: BLEChunking.Error.missingFirstChunk) {
            try reassembler.append(chunks[3])
        }

        // A new first chunk starts over cleanly.
        var result: [UInt8]?
        for chunk in chunks {
            result = try reassembler.append(chunk)
        }
        #expect(result == [UInt8](repeating: 5, count: 100))
    }
}

struct BLEChannelNonceTests {
    @Test
    func nonceLayout() {
        let nonce = BLEChannelNonce.make(sender: .phone, counter: 0x0102_0304_0506_0708)
        #expect(nonce == [0x01, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8])
    }

    @Test
    func countersAdvanceAndRejectReplays() throws {
        var phone = BLEChannelCounters()
        var pc = BLEChannelCounters()

        let first = phone.nextNonce(from: .phone)
        let second = phone.nextNonce(from: .phone)
        try pc.accept(first, from: .phone)
        try pc.accept(second, from: .phone)
        #expect(pc.lastReceived == 2)

        #expect(throws: BLEChannelNonce.Error.replayed(counter: 2, lastAccepted: 2)) {
            try pc.accept(second, from: .phone)
        }
        #expect(throws: BLEChannelNonce.Error.unexpectedSender(0x02)) {
            try pc.accept(BLEChannelNonce.make(sender: .pc, counter: 9), from: .phone)
        }
        #expect(throws: BLEChannelNonce.Error.truncated) {
            try pc.accept([1, 0, 0], from: .phone)
        }
    }

    @Test
    func countersSurviveEncoding() throws {
        var counters = BLEChannelCounters()
        _ = counters.nextNonce(from: .phone)
        let restored = try JSONDecoderShim.roundTrip(counters)
        #expect(restored == counters)
    }
}

struct HandshakeTests {
    func pcHello(versions: [UInt32] = [1], capabilities: [Fenpo_V1_Capability] = [.clipboard, .ring, .camera, .notificationMirror]) -> Fenpo_V1_Hello {
        var hello = Fenpo_V1_Hello()
        hello.deviceID = "pc-7f3a"
        hello.deviceName = "BUGRA-PC"
        hello.platform = .windows
        hello.protocolVersions = versions
        hello.capabilities = capabilities
        return hello
    }

    @Test
    func agreesOnTheCommonFeatures() throws {
        let local = Handshake.makeHello(deviceID: "phone", deviceName: "iPhone", appVersion: "0.1.0")
        var sequencer = EnvelopeSequencer()
        let session = try Handshake.negotiate(local: local, remote: sequencer.envelope(.hello(pcHello())))
        #expect(session.protocolVersion == 1)
        #expect(session.capabilities == [.clipboard, .ring])
        #expect(session.peerPlatform == .windows)
        #expect(session.peerName == "BUGRA-PC")
    }

    @Test
    func picksTheHighestCommonVersion() throws {
        var local = Handshake.makeHello(deviceID: "phone", deviceName: "iPhone", appVersion: "0.1.0")
        local.protocolVersions = [1, 2, 3]
        let session = try Handshake.negotiate(local: local, remote: pcHello(versions: [1, 2, 4]))
        #expect(session.protocolVersion == 2)
    }

    @Test
    func failsWithoutACommonVersion() {
        let local = Handshake.makeHello(deviceID: "phone", deviceName: "iPhone", appVersion: "0.1.0")
        #expect(throws: Handshake.Error.noCommonVersion(remote: [7])) {
            try Handshake.negotiate(local: local, remote: pcHello(versions: [7]))
        }
    }

    @Test
    func reportsPeerErrorsAndUnexpectedMessages() {
        let local = Handshake.makeHello(deviceID: "phone", deviceName: "iPhone", appVersion: "0.1.0")
        var sequencer = EnvelopeSequencer()
        var error = Fenpo_V1_Error()
        error.code = .notPaired
        error.message = "unknown certificate"
        #expect(throws: Handshake.Error.peerError(.notPaired, "unknown certificate")) {
            try Handshake.negotiate(local: local, remote: sequencer.envelope(.error(error)))
        }
        #expect(throws: Handshake.Error.expectedHello) {
            try Handshake.negotiate(local: local, remote: sequencer.envelope(.ping(Fenpo_V1_Ping())))
        }
    }

    @Test
    func sequencerNumbersFromOneAndAnswersPing() {
        var sequencer = EnvelopeSequencer()
        var ping = Fenpo_V1_Ping()
        ping.nonce = 42
        let request = sequencer.envelope(.ping(ping))
        #expect(request.id == 1)
        #expect(request.version == FenpoProtocolVersion.current)

        let reply = Handshake.automaticReply(to: request, sequencer: &sequencer)
        #expect(reply?.id == 2)
        #expect(reply?.replyTo == 1)
        #expect(reply?.pong.nonce == 42)
        #expect(Handshake.automaticReply(to: sequencer.envelope(.lockPc(Fenpo_V1_LockPc())), sequencer: &sequencer) == nil)
    }
}

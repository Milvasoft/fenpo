import FenpoCore
import Testing

struct AnnexBTests {
    @Test
    func rewritesLengthPrefixesAsStartCodes() {
        let avcc: [UInt8] = [0, 0, 0, 2, 0x65, 0xAA, 0, 0, 0, 3, 0x41, 0xBB, 0xCC]
        #expect(AnnexB.from(avcc: avcc) == [0, 0, 0, 1, 0x65, 0xAA, 0, 0, 0, 1, 0x41, 0xBB, 0xCC])
    }

    @Test
    func supportsShortPrefixesAndStopsAtTruncation() {
        #expect(AnnexB.from(avcc: [0, 1, 0x67], lengthSize: 2) == [0, 0, 0, 1, 0x67])
        #expect(AnnexB.from(avcc: [0, 0, 0, 9, 0x65]) == [])
        #expect(AnnexB.from(avcc: [1, 2, 3], lengthSize: 3) == [])
    }
}

struct MediaPacketQueueTests {
    typealias Queue = MediaPacketQueue<String>

    func video(_ name: String, key: Bool = false, config: Bool = false) -> Queue.Entry {
        Queue.Entry(packet: name, isVideo: true, isKeyFrame: key, isCodecConfig: config)
    }

    func audio(_ name: String) -> Queue.Entry {
        Queue.Entry(packet: name, isVideo: false, isKeyFrame: false, isCodecConfig: false)
    }

    @Test
    func keepsOrderWhenTheConnectionKeepsUp() {
        var queue = Queue()
        for entry in [video("c", key: true, config: true), video("k", key: true), video("p1"), audio("a1")] {
            queue.push(entry)
        }
        var out: [String] = []
        while let next = queue.pop() {
            out.append(next)
        }
        #expect(out == ["c", "k", "p1", "a1"])
    }

    @Test
    func aKeyFrameReplacesWaitingVideoButKeepsAudioAndConfig() {
        var queue = Queue()
        queue.push(video("p1"))
        queue.push(audio("a1"))
        queue.push(video("p2"))
        queue.push(video("c", key: true, config: true))
        queue.push(video("k", key: true))
        var out: [String] = []
        while let next = queue.pop() {
            out.append(next)
        }
        #expect(out == ["a1", "c", "k"])
        #expect(queue.droppedVideoFrames == 2)
    }

    @Test
    func dropsTheOldestOrdinaryFramesBeyondTheLimit() {
        var queue = Queue(maxVideoFrames: 2)
        queue.push(video("k", key: true))
        for index in 1...4 {
            queue.push(video("p\(index)"))
        }
        var out: [String] = []
        while let next = queue.pop() {
            out.append(next)
        }
        #expect(out == ["k", "p3", "p4"])
        #expect(queue.droppedVideoFrames == 2)
    }
}

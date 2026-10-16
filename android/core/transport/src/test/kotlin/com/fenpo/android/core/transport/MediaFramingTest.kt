package com.fenpo.android.core.transport

import com.fenpo.protocol.v1.MediaStreamOpen
import com.fenpo.protocol.v1.StreamKind
import com.fenpo.protocol.v1.StreamPacket
import com.fenpo.protocol.v1.StreamTrack
import com.fenpo.protocol.v1.VideoCodec
import com.fenpo.protocol.v1.VideoFormat
import okio.Buffer
import okio.ByteString.Companion.encodeUtf8
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class MediaFramingTest {
    @Test
    fun opensWithAnEnvelopeThenCarriesPackets() {
        val buffer = Buffer()
        val open =
            MediaStreamOpen(
                stream_id = "s",
                kind = StreamKind.STREAM_KIND_CAMERA,
                video = VideoFormat(codec = VideoCodec.VIDEO_CODEC_H264, width = 1280, height = 720, fps = 30),
            )
        val packets =
            listOf(
                StreamPacket(track = StreamTrack.STREAM_TRACK_VIDEO, codec_config = true, data_ = "sps".encodeUtf8()),
                StreamPacket(
                    track = StreamTrack.STREAM_TRACK_VIDEO,
                    pts_us = 33_000,
                    key_frame = true,
                    data_ = "idr".encodeUtf8(),
                ),
                StreamPacket(track = StreamTrack.STREAM_TRACK_AUDIO, pts_us = 40_000, data_ = "aac".encodeUtf8()),
            )

        MediaFraming.writeOpen(buffer, open)
        packets.forEach { MediaFraming.writePacket(buffer, it) }

        assertEquals(open, MediaFraming.readOpen(buffer))
        assertEquals(packets, List(3) { MediaFraming.readPacket(buffer) })
        assertNull(MediaFraming.readPacket(buffer))
    }
}

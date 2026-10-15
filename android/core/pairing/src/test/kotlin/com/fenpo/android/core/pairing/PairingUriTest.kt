package com.fenpo.android.core.pairing

import com.fenpo.android.core.transport.Endpoint
import okio.ByteString.Companion.decodeHex
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

class PairingUriTest {
    private val fingerprint = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
    private val token = "AAECAwQFBgcICQoLDA0ODw" // 00 01 02 … 0f, base64url without padding

    private fun code(
        v: String = "1",
        a: String = "192.168.1.20:52000,[fe80::1c2b:3aff:fe11:2233]:52000",
        fp: String = fingerprint,
        t: String = token,
    ) = "fenpo://pair?v=$v&id=pc-7f3a&n=BUGRA-PC%20%C3%87al%C4%B1%C5%9Fma&a=$a&fp=$fp&t=$t"

    @Test
    fun parsesAValidCode() {
        val result = assertIs<PairingUriResult.Valid>(PairingUri.parse(code()))

        with(result.uri) {
            assertEquals(1, version)
            assertEquals("pc-7f3a", pcId)
            assertEquals("BUGRA-PC Çalışma", pcName)
            assertEquals(
                listOf(
                    Endpoint("192.168.1.20", 52000),
                    Endpoint("fe80::1c2b:3aff:fe11:2233", 52000),
                ),
                addresses,
            )
            assertEquals(fingerprint.decodeHex(), certificateSha256)
            assertEquals("000102030405060708090a0b0c0d0e0f".decodeHex(), token)
        }
    }

    @Test
    fun acceptsUppercaseFingerprint() {
        assertIs<PairingUriResult.Valid>(PairingUri.parse(code(fp = fingerprint.uppercase())))
    }

    @Test
    fun rejectsOtherQrCodes() {
        assertEquals(PairingUriResult.Invalid(PairingUriError.NOT_FENPO), PairingUri.parse("https://fenpo.app"))
        assertEquals(PairingUriResult.Invalid(PairingUriError.NOT_FENPO), PairingUri.parse("WIFI:S:Ev-5G;T:WPA;P:x;;"))
    }

    @Test
    fun rejectsUnsupportedVersion() {
        assertEquals(PairingUriResult.Invalid(PairingUriError.UNSUPPORTED_VERSION), PairingUri.parse(code(v = "2")))
    }

    @Test
    fun rejectsBadAddresses() {
        listOf("192.168.1.20", "192.168.1.20:0", "192.168.1.20:70000", "fe80::1:52000", "[fe80::1]52000").forEach {
            assertEquals(PairingUriResult.Invalid(PairingUriError.BAD_ADDRESS), PairingUri.parse(code(a = it)), it)
        }
    }

    @Test
    fun rejectsShortFingerprintAndToken() {
        assertEquals(PairingUriResult.Invalid(PairingUriError.BAD_FINGERPRINT), PairingUri.parse(code(fp = "9f86")))
        assertEquals(PairingUriResult.Invalid(PairingUriError.BAD_TOKEN), PairingUri.parse(code(t = "AAEC")))
    }

    @Test
    fun rejectsMissingFieldsAndDuplicates() {
        assertEquals(
            PairingUriResult.Invalid(PairingUriError.MISSING_FIELD),
            PairingUri.parse("fenpo://pair?v=1&id=pc&a=10.0.0.2:1&fp=$fingerprint&t=$token"),
        )
        assertEquals(PairingUriResult.Invalid(PairingUriError.MALFORMED), PairingUri.parse(code() + "&v=1"))
    }
}

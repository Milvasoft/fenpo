package com.fenpo.android.core.transport.ble

import com.fenpo.android.core.transport.ble.BleChannelCrypto.Sender
import okio.ByteString.Companion.decodeHex
import okio.ByteString.Companion.toByteString
import java.security.GeneralSecurityException
import kotlin.random.Random
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class BleChannelTest {
    private val key = ByteArray(32) { it.toByte() }

    @Test
    fun phoneToPcRoundTripThroughFragments() {
        val phone = BleChannelCrypto(key, Sender.PHONE)
        val pc = BleChannelCrypto(key, Sender.PC)
        val envelope = Random(7).nextBytes(1500)

        for (mtu in listOf(23, 185, 247, 517)) {
            val reassembler = BleReassembler(maxMessageBytes = BleChannelCrypto.MAX_SEALED_BYTES)
            val fragments = BleFragmenter.split(phone.seal(envelope), mtu)
            fragments.forEach { assert(it.size <= mtu - BleFragmenter.ATT_OVERHEAD) }

            val joined = fragments.mapNotNull { reassembler.accept(it) }.single()
            assertContentEquals(envelope, pc.open(joined), "MTU $mtu")
        }
    }

    @Test
    fun sealedLayoutIsNonceCiphertextTag() {
        // protocol/README.md: nonce (12) || ciphertext || tag (16). Phone, counter 1, empty plaintext.
        val sealed = BleChannelCrypto(key, Sender.PHONE).seal(ByteArray(0))
        assertEquals("010000000000000000000001", sealed.copyOfRange(0, 12).toByteString().hex())
        assertEquals(28, sealed.size)
        assertContentEquals(ByteArray(0), BleChannelCrypto(key, Sender.PC).open(sealed))
    }

    @Test
    fun matchesAKnownVector() {
        // Independent AES-GCM (javax.crypto, same key, nonce and AAD) must give the same bytes as
        // the Windows and iPhone implementations; this pins the layout against accidental change.
        val sealed = BleChannelCrypto(key, Sender.PC, nextSendCounter = 7).seal("hi".toByteArray())
        val cipher = javax.crypto.Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(
            javax.crypto.Cipher.ENCRYPT_MODE,
            javax.crypto.spec.SecretKeySpec(key, "AES"),
            javax.crypto.spec.GCMParameterSpec(128, "020000000000000000000007".decodeHex().toByteArray()),
        )
        cipher.updateAAD("fenpo-ble-v1".toByteArray())
        val expected = "020000000000000000000007".decodeHex().toByteArray() + cipher.doFinal("hi".toByteArray())
        assertContentEquals(expected, sealed)
    }

    @Test
    fun rejectsReplayTamperingAndWrongDirection() {
        val phone = BleChannelCrypto(key, Sender.PHONE)
        val pc = BleChannelCrypto(key, Sender.PC)
        val first = phone.seal("hello".toByteArray())
        pc.open(first)

        assertFailsWith<GeneralSecurityException> { pc.open(first) }

        val tampered = phone.seal("hello".toByteArray()).also { it[it.size - 1] = (it[it.size - 1] + 1).toByte() }
        assertFailsWith<GeneralSecurityException> { pc.open(tampered) }

        // The phone's own message reflected back must not decrypt as if it came from the PC.
        assertFailsWith<GeneralSecurityException> {
            BleChannelCrypto(
                key,
                Sender.PHONE,
            ).open(phone.seal(byteArrayOf(1)))
        }
    }

    @Test
    fun countersSurviveRestart() {
        val phone = BleChannelCrypto(key, Sender.PHONE, nextSendCounter = 41)
        val pc = BleChannelCrypto(key, Sender.PC, lastReceivedCounter = 40)

        pc.open(phone.seal(byteArrayOf(1)))

        assertEquals(42, phone.nextSendCounter)
        assertEquals(41, pc.lastReceivedCounter)
    }

    @Test
    fun fragmentHeaders() {
        val fragments = BleFragmenter.split("000102030405".decodeHex().toByteArray(), mtu = 3 + 3 + 4)

        assertEquals(listOf("01000000010203", "0200010405"), fragments.map { it.toByteString().hex() })
        assertEquals(listOf("03000041"), BleFragmenter.split(byteArrayOf(0x41), 23).map { it.toByteString().hex() })
    }

    @Test
    fun reassemblerDropsBrokenMessages() {
        val reassembler = BleReassembler(maxMessageBytes = 100)
        val parts = BleFragmenter.split(ByteArray(20) { it.toByte() }, mtu = 3 + 3 + 8)

        // Lost middle fragment: nothing comes out, and the next whole message still does.
        reassembler.accept(parts[0])
        assertNull(reassembler.accept(parts[2]))
        assertEquals(1, reassembler.dropped)

        val whole = parts.mapNotNull { reassembler.accept(it) }.single()
        assertContentEquals(ByteArray(20) { it.toByte() }, whole)

        // A continuation without a start is ignored.
        assertNull(reassembler.accept(parts[1]))
    }

    @Test
    fun reassemblerEnforcesSizeLimit() {
        val reassembler = BleReassembler(maxMessageBytes = 10)
        assertNull(BleFragmenter.split(ByteArray(30), mtu = 23).firstNotNullOfOrNull { reassembler.accept(it) })
        assertEquals(1, reassembler.dropped)
    }
}

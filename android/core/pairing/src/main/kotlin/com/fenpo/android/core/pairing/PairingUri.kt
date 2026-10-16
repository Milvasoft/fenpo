package com.fenpo.android.core.pairing

import com.fenpo.android.core.protocol.ProtocolVersion
import com.fenpo.android.core.transport.Endpoint
import okio.ByteString
import okio.ByteString.Companion.decodeBase64
import okio.ByteString.Companion.decodeHex
import java.net.URLDecoder

/**
 * Contents of the pairing QR code shown by the PC (protocol/fenpo/v1/pairing.proto):
 *
 * `fenpo://pair?v=1&id=<pcId>&n=<name>&a=<ip:port,ip:port>&fp=<hex sha256>&t=<base64url token>`
 *
 * @property version Protocol version of the code (`v`).
 * @property pcId Stable id of the PC (`id`).
 * @property pcName User visible PC name (`n`).
 * @property addresses Data channel endpoints to try, in the PC's order (`a`).
 * @property certificateSha256 SHA-256 of the PC's TLS certificate (`fp`), 32 bytes.
 * @property token One-time pairing token (`t`), 16 bytes.
 */
public data class PairingUri(
    val version: Int,
    val pcId: String,
    val pcName: String,
    val addresses: List<Endpoint>,
    val certificateSha256: ByteString,
    val token: ByteString,
) {
    public companion object {
        private const val PREFIX = "fenpo://pair?"
        private const val FINGERPRINT_BYTES = 32
        private const val TOKEN_BYTES = 16

        /** Parses scanned QR text. Never throws; invalid input returns [PairingUriResult.Invalid]. */
        public fun parse(text: String): PairingUriResult {
            val trimmed = text.trim()
            if (!trimmed.startsWith(PREFIX, ignoreCase = true)) return invalid(PairingUriError.NOT_FENPO)

            val params = parseQuery(trimmed.substring(PREFIX.length)) ?: return invalid(PairingUriError.MALFORMED)

            val version = params["v"]?.toIntOrNull() ?: return invalid(PairingUriError.MISSING_FIELD)
            if (version !in ProtocolVersion.supported) return invalid(PairingUriError.UNSUPPORTED_VERSION)

            val pcId = params["id"]?.takeIf { it.isNotBlank() } ?: return invalid(PairingUriError.MISSING_FIELD)
            val pcName = params["n"]?.takeIf { it.isNotBlank() } ?: return invalid(PairingUriError.MISSING_FIELD)

            val addresses =
                params["a"]
                    ?.split(',')
                    ?.filter { it.isNotBlank() }
                    ?.map { Endpoint.parse(it) ?: return invalid(PairingUriError.BAD_ADDRESS) }
                    ?.takeIf { it.isNotEmpty() }
                    ?: return invalid(PairingUriError.MISSING_FIELD)

            val fingerprint =
                params["fp"]
                    ?.takeIf { it.length == FINGERPRINT_BYTES * 2 }
                    ?.let { hex -> hex.lowercase().takeIf { it.all(Char::isHexDigit) }?.decodeHex() }
                    ?: return invalid(PairingUriError.BAD_FINGERPRINT)

            val token =
                params["t"]
                    ?.decodeBase64()
                    ?.takeIf { it.size == TOKEN_BYTES }
                    ?: return invalid(PairingUriError.BAD_TOKEN)

            return PairingUriResult.Valid(PairingUri(version, pcId, pcName, addresses, fingerprint, token))
        }

        private fun invalid(error: PairingUriError) = PairingUriResult.Invalid(error)

        private fun parseQuery(query: String): Map<String, String>? {
            val result = HashMap<String, String>()
            for (part in query.split('&')) {
                if (part.isEmpty()) continue
                val eq = part.indexOf('=')
                if (eq <= 0) return null
                val key = part.substring(0, eq)
                val value =
                    decode(part.substring(eq + 1)) ?: return null
                if (result.put(key, value) != null) return null
            }
            return result
        }

        /**
         * Percent-decodes one value. Uses the `String` charset overload on purpose: the
         * `Charset` one only exists from Android 13, and this module runs on Android 12 too
         * (device test, 16 October). Only a bad escape is caught, so a missing API still fails loudly.
         */
        private fun decode(value: String): String? =
            try {
                URLDecoder.decode(value, "UTF-8")
            } catch (_: IllegalArgumentException) {
                null
            }
    }
}

/** Outcome of [PairingUri.parse]. */
public sealed interface PairingUriResult {
    /** The text is a usable Fenpo pairing code. */
    public data class Valid(
        val uri: PairingUri,
    ) : PairingUriResult

    /** The text is not a usable pairing code; [error] says why. */
    public data class Invalid(
        val error: PairingUriError,
    ) : PairingUriResult
}

/** Why a scanned code was rejected. */
public enum class PairingUriError {
    /** Some other QR code (a link, a Wi‑Fi code). */
    NOT_FENPO,

    /** The query string cannot be read. */
    MALFORMED,

    /** A required parameter is missing or empty. */
    MISSING_FIELD,

    /** The PC speaks a protocol version this app does not support. */
    UNSUPPORTED_VERSION,

    /** An address is not `host:port` or `[ipv6]:port`. */
    BAD_ADDRESS,

    /** The certificate fingerprint is not 32 bytes of hex. */
    BAD_FINGERPRINT,

    /** The token is not 16 bytes of base64url. */
    BAD_TOKEN,
}

private fun Char.isHexDigit(): Boolean = this in '0'..'9' || this in 'a'..'f'

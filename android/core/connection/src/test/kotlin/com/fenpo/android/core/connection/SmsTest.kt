package com.fenpo.android.core.connection

import android.provider.Telephony
import com.fenpo.android.core.connection.sms.Sms
import com.fenpo.protocol.v1.SmsStatus
import org.junit.Test
import kotlin.test.assertEquals

class SmsTest {
    @Test
    fun mapsAndroidMessageTypes() {
        assertEquals(SmsStatus.SMS_STATUS_RECEIVED, Sms.status(Telephony.Sms.MESSAGE_TYPE_INBOX))
        assertEquals(SmsStatus.SMS_STATUS_SENT, Sms.status(Telephony.Sms.MESSAGE_TYPE_SENT))
        assertEquals(SmsStatus.SMS_STATUS_PENDING, Sms.status(Telephony.Sms.MESSAGE_TYPE_OUTBOX))
        assertEquals(SmsStatus.SMS_STATUS_PENDING, Sms.status(Telephony.Sms.MESSAGE_TYPE_QUEUED))
        assertEquals(SmsStatus.SMS_STATUS_FAILED, Sms.status(Telephony.Sms.MESSAGE_TYPE_FAILED))
        assertEquals(SmsStatus.SMS_STATUS_UNSPECIFIED, Sms.status(Telephony.Sms.MESSAGE_TYPE_DRAFT))
    }
}

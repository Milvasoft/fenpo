package com.fenpo.android.core.connection

import com.fenpo.android.core.connection.transfer.FileNames
import org.junit.Test
import kotlin.test.assertEquals

class FileNamesTest {
    @Test
    fun keepsOnlyASafeBaseName() {
        assertEquals("passwd", FileNames.safe("../../etc/passwd"))
        assertEquals("report.pdf", FileNames.safe("""C:\Users\me\report.pdf"""))
        assertEquals("hidden", FileNames.safe(".hidden"))
        assertEquals("ab.txt", FileNames.safe("a:b?.txt"))
        assertEquals("file", FileNames.safe("..."))
        assertEquals("Sözleşme v3.pdf", FileNames.safe("Sözleşme v3.pdf"))
    }
}

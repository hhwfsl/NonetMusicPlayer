package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.*
import org.junit.Assert.*
import org.junit.Test

/** 扫描边界规则不依赖 Android 文件系统。 */
class ScanRulesTests {
    @Test fun durationIncludesBoundaryAndZeroDisablesFilter() {
        assertTrue(ScanOptions().acceptsDuration(0))
        assertFalse(ScanOptions(60).acceptsDuration(59999))
        assertTrue(ScanOptions(60).acceptsDuration(60000))
        assertFalse(ScanOptions(60).acceptsDuration(0))
    }
    @Test fun foldersUseSegmentAndVolumeBoundaries() {
        val music=ScanLocation.documentId("primary:Music/")!!
        assertTrue(music.contains(ScanLocation.documentId("external_primary:Music/Short")!!))
        assertFalse(music.contains(ScanLocation.documentId("primary:Music2")!!))
        assertFalse(music.contains(ScanLocation.documentId("1234-ABCD:Music/Short")!!))
        assertTrue(ScanLocation.documentId("primary:")!!.contains(music))
        assertNull(ScanLocation.documentId("primary:Music/../Private"))
    }
}

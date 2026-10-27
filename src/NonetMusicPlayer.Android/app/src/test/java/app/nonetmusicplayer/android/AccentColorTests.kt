package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.AccentColor
import org.junit.Assert.*
import org.junit.Test

class AccentColorTests {
    @Test fun sixDigitsAreOpaqueAndEightDigitsUseArgb() {
        assertEquals(0xFF123456L,AccentColor.parse("#123456"))
        assertEquals(0x80123456L,AccentColor.parse("#80123456"))
        assertEquals(0x00123456L,AccentColor.parse("#00123456"))
        assertEquals("#80123456",AccentColor.format(0x80123456L))
    }
    @Test fun invalidCodesAndOutOfRangeChannelsAreRejected() {
        listOf("red","123456","#12345","#1234567","#GG0000","#123456789").forEach {assertNull(it,AccentColor.parse(it))}
        assertNull(AccentColor.channels(-1,0,0,0));assertNull(AccentColor.channels(255,256,0,0))
        assertEquals(0x7F102030L,AccentColor.channels(127,16,32,48))
    }
}

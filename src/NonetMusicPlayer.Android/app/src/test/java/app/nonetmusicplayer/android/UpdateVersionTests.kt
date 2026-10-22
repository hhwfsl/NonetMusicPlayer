package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.updates.UpdateVersion
import org.junit.Assert.*
import org.junit.Test

/** 发布版本排序独立于 Android 框架与 GitHub 返回顺序。 */
class UpdateVersionTests {
    @Test fun numericPrereleasesDoNotUseLexicographicOrder() {
        assertTrue(UpdateVersion.parse("0.3.0-beta.10")!!>UpdateVersion.parse("0.3.0-beta.9")!!)
        assertTrue(UpdateVersion.parse("0.3.0")!!>UpdateVersion.parse("0.3.0-rc.99")!!)
        assertTrue(UpdateVersion.parse("1.0.0")!!>UpdateVersion.parse("0.99.99")!!)
        assertEquals(UpdateVersion.parse("1.0.0+first"),UpdateVersion.parse("1.0.0+second"))
    }
    @Test fun malformedAndOverflowVersionsAreRejected() {
        listOf("","android-v1.0.0","1.2","1.2.3.4","2147483648.0.0","-1.0.0","1.0.0-beta.","1.0.0/evil").forEach {assertNull(it,UpdateVersion.parse(it))}
    }
}

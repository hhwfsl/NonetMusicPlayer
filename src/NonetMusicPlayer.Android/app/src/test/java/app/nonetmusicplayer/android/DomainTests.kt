package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.*
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import kotlin.random.Random

class DomainTests {
    @Test fun lrcFormatsTranslationOffsetAndBlanks() {
        val lines=LrcParser.parse("[offset:+100]\n[00:00]原文\n[00:00]Translation\n[00:01.25]二\n[00:02:345]三\n[00:03;5]四\n[00:04.000]\n[00:05][00:06]重复")
        assertEquals(listOf(100L,1350L,2445L,3600L,5100L,6100L),lines.map {it.timeMs})
        assertEquals("Translation",lines[0].translation);assertEquals(2,LrcParser.currentIndex(lines,2500))
        assertTrue(lines.none {it.text.isBlank()});assertEquals(listOf("一","二"),LrcParser.parse("一\n\n二").map {it.text})
    }
    @Test fun wordLyricsAndTranslationsPreserveTextAndTiming() {
        val lines=LrcParser.parse("[00:01.000]光[00:01.300]の[00:01.600]道[00:02.000]\n[00:01.000]道路[00:02.000]\n[00:03.000]<00:03.000>hello<00:03.500> world<00:04.000>\n[00:05.000]")
        assertEquals(2,lines.size);assertEquals("光の道",lines.first().text)
        assertEquals("道路",lines.first().translation);assertEquals(3,lines.first().words.size)
        assertEquals(1300L,lines.first().words[1].startMs);assertEquals(2000L,lines.first().words.last().endMs)
        assertEquals("hello world",lines.last().text);assertEquals(3500L,lines.last().words.last().startMs)
    }
    @Test fun weightedShuffleIsBoundedAndNeverImmediatelyRepeats() {
        val selector=WeightedShuffle(Random(18));val ids=listOf("a","b","c")
        assertNull(selector.choose(emptyList(),null));assertEquals("a",selector.choose(listOf("a"),"a"))
        var current="a";val counts=mutableMapOf<String,Int>()
        repeat(10000) {val next=selector.choose(ids,current)!!;assertNotEquals(current,next);counts[next]=(counts[next] ?: 0)+1;current=next}
        assertEquals(ids.toSet(),counts.keys);assertTrue(counts.values.all {it in 3000..3700})
    }
    @Test fun documentsAreBoundedAndDoNotUseNewAndroidApi() {
        assertArrayEquals(byteArrayOf(1,2),ByteArrayInputStream(byteArrayOf(1,2)).readLimited(2))
        assertThrows(IllegalArgumentException::class.java) {ByteArrayInputStream(ByteArray(100)).readLimited(20)}
    }
}

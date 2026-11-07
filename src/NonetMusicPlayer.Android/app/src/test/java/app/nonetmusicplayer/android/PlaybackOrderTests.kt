package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.PlaybackOrder
import org.junit.Assert.*
import org.junit.Test
import kotlin.random.Random

/** 验证可见队列就是实际播放顺序，随机只发生在建立队列时。 */
class PlaybackOrderTests {
    @Test fun leavingShuffleRestoresSourceOrderWithoutChangingMembers() {
        val source=listOf("a","b","c","d")
        assertEquals(source,PlaybackOrder.restore(listOf("d","b","a","c"),source))
        assertEquals(listOf("a","c","extra"),PlaybackOrder.restore(listOf("c","extra","a"),source))
        assertEquals(emptyList<String>(),PlaybackOrder.restore(emptyList(),source))
    }
    @Test fun shuffleVisitsEverySongBeforeWrappingAndPreviousRetraces() {
        val ids=List(40) {"song-$it"}
        val order=PlaybackOrder.create(ids+ids,"song-13",true,Random(9))
        assertEquals("song-13",order.first());assertEquals(ids.toSet(),order.toSet());assertEquals(ids.size,order.size)
        assertNotEquals(ids,order)
        val visited=mutableListOf(order.first());var current=order.first()
        repeat(order.size-1) {current=PlaybackOrder.next(order,current,1)!!;visited+=current}
        assertEquals(order,visited);assertEquals(order.first(),PlaybackOrder.next(order,current,1))
        for(i in order.lastIndex downTo 1) {assertEquals(order[i-1],PlaybackOrder.next(order,order[i],-1))}
    }
    @Test fun manualSelectionReshufflesWithSelectedFirst() {
        val ids=List(16) {it.toString()};val random=Random(45)
        val first=PlaybackOrder.create(ids,"4",true,random)
        val second=PlaybackOrder.create(first,"4",true,random)
        assertEquals("4",second.first());assertEquals(first.toSet(),second.toSet());assertNotEquals(first,second)
        assertEquals(ids,PlaybackOrder.create(ids+ids,"4",false))
    }
    @Test fun draggingChangesNextSongWithoutChangingMembershipOrCurrent() {
        val order=listOf("a","b","c","d")
        val moved=PlaybackOrder.move(order,"d",-2)
        assertEquals(listOf("a","d","b","c"),moved);assertEquals("d",PlaybackOrder.next(moved,"a",1))
        assertEquals("a",PlaybackOrder.next(moved,"d",-1));assertEquals("a",PlaybackOrder.next(moved,"a",1,true))
        assertEquals(listOf("b","c","d","a"),PlaybackOrder.move(order,"a",Int.MAX_VALUE))
        assertEquals(listOf("d","a","b","c"),PlaybackOrder.move(order,"d",Int.MIN_VALUE))
        assertEquals(order,PlaybackOrder.move(order,"missing",1))
    }
    @Test fun emptySingleAndMissingCurrentAreHandled() {
        assertNull(PlaybackOrder.next(emptyList(),null,1))
        assertEquals(listOf("a"),PlaybackOrder.create(listOf("a","a"),"a",true))
        assertEquals("a",PlaybackOrder.next(listOf("a"),"a",-1))
        assertEquals("a",PlaybackOrder.next(listOf("a","b"),"missing",1))
        assertEquals("b",PlaybackOrder.next(listOf("a","b"),"missing",-1))
    }
}

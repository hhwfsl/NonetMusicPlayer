package app.nonetmusicplayer.android.domain

import kotlin.random.Random

/** 随机模式只在手动选歌或进入模式时洗牌；自动播放、上一首与下一首始终沿实际队列移动。 */
object PlaybackOrder {
    /** 只恢复当前队列成员的来源顺序，保留队列独有歌曲，不把浏览页或新加入的歌曲混入。 */
    fun restore(ids: List<String>,source: List<String>): List<String> {
        val members=ids.toSet()
        return (source.filter {it in members}+ids).distinct()
    }
    fun create(ids: List<String>,selected: String?,shuffle: Boolean,random: Random=Random.Default): List<String> {
        val unique=ids.distinct()
        if(!shuffle) return unique
        val remaining=unique.filter {it!=selected}.shuffled(random)
        return if(selected in unique) listOf(selected!!)+remaining else remaining
    }
    fun move(ids: List<String>,id: String,delta: Int): List<String> {
        val from=ids.indexOf(id)
        if(from<0) return ids
        return ids.toMutableList().apply {add((from.toLong()+delta).coerceIn(0,lastIndex.toLong()).toInt(),removeAt(from))}
    }
    fun next(ids: List<String>,current: String?,direction: Int,repeatCurrent: Boolean=false): String? {
        if(ids.isEmpty()) return null
        if(repeatCurrent && current in ids) return current
        val from=ids.indexOf(current)
        if(from<0) return if(direction<0) ids.last() else ids.first()
        return ids[Math.floorMod(from+direction,ids.size)]
    }
}

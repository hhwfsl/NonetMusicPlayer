package app.nonetmusicplayer.android.domain

import kotlin.random.Random

/** 与桌面行为一致：历史降权但不归零，多首时排除刚播放的一首。 */
class WeightedShuffle(private val random: Random = Random.Default) {
    private val counts = mutableMapOf<String, Int>()
    fun choose(ids: List<String>, current: String?): String? {
        val distinct = ids.distinct()
        if (distinct.isEmpty()) return null
        counts.keys.retainAll(distinct.toSet())
        val candidates = if (distinct.size == 1) distinct else distinct.filter { it != current }
        var draw = random.nextDouble() * candidates.sumOf { 1.0 / (1 + (counts[it] ?: 0)) }
        val selected = candidates.firstOrNull { draw -= 1.0 / (1 + (counts[it] ?: 0)); draw < 0 } ?: candidates.last()
        counts[selected] = ((counts[selected] ?: 0) + 1).coerceAtMost(1_000_000)
        return selected
    }
}

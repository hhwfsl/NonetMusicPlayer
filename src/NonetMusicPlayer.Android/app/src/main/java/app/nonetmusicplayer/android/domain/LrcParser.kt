package app.nonetmusicplayer.android.domain

data class LyricWord(val startMs: Long,val endMs: Long,val start: Int,val end: Int)
data class LyricLine(val timeMs: Long,val text: String,val translation: String="",
    val words: List<LyricWord> = emptyList(),val translationWords: List<LyricWord> = emptyList(),val endMs: Long = 0)

/** 兼容逐行、Enhanced LRC 与方括号逐字时间；同时间原文/译文保持文件顺序。 */
object LrcParser {
    private val tag=Regex("""\[(\d{1,4}):([0-5]\d)(?:[.:;](\d{1,3}))?]""")
    private val inline=Regex("""[<\[](\d{1,4}):([0-5]\d)(?:[.:;](\d{1,3}))?[>\]]""")
    private fun timestamp(match: MatchResult,offset: Long): Long {
        val fraction=match.groupValues[3].padEnd(3,'0').take(3).toLongOrNull() ?: 0
        return (match.groupValues[1].toLong()*60000+match.groupValues[2].toLong()*1000+fraction+offset).coerceAtLeast(0)
    }
    fun parse(text: String): List<LyricLine> {
        val offset=Regex("""(?im)^\s*\[offset:([+-]?\d+)]""").find(text)?.groupValues?.get(1)?.toLongOrNull() ?: 0
        val timed=mutableListOf<LyricLine>();val plain=mutableListOf<LyricLine>()
        text.removePrefix("\uFEFF").lineSequence().forEach {raw ->
            val line=raw.trim();var end=0;val times=mutableListOf<Long>()
            for(match in tag.findAll(line)) {
                if(match.range.first!=end) break
                end=match.range.last+1;times+=timestamp(match,offset)
            }
            val content=line.substring(end)
            if(times.isEmpty()) {
                if(content.isNotBlank() && !content.startsWith("[")) plain+=LyricLine(-1,content)
            } else {
                val markers=inline.findAll(content).toList();val words=mutableListOf<LyricWord>();val result=StringBuilder()
                if(markers.isEmpty()) result.append(content.trim())
                else {
                    // 第一个时间戳到下一时间戳之间的文字为第一片段；末尾空时间戳只标记结束。
                    val firstText=content.substring(0,markers.first().range.first)
                    if(firstText.isNotEmpty()) {
                        result.append(firstText);words+=LyricWord(times.first(),timestamp(markers.first(),offset),0,result.length)
                    }
                    markers.forEachIndexed {i,marker ->
                        val next=markers.getOrNull(i+1)
                        val chunk=content.substring(marker.range.last+1,next?.range?.first ?: content.length)
                        val start=result.length;result.append(chunk)
                        if(chunk.isNotEmpty()) words+=LyricWord(timestamp(marker,offset),next?.let {timestamp(it,offset)} ?: timestamp(marker,offset)+1000,start,result.length)
                    }
                }
                if(result.isNotBlank()) times.forEach {time ->
                    val shift=time-times.first()
                    timed+=LyricLine(time,result.toString(),words=words.map {it.copy(startMs=it.startMs+shift,endMs=it.endMs+shift)},endMs=words.lastOrNull()?.endMs?.plus(shift) ?: 0)
                }
            }
        }
        if(timed.isEmpty()) return plain
        val grouped=timed.groupBy {it.timeMs}.toSortedMap().map {(_,lines) ->
            val unique=lines.distinctBy {it.text};val original=unique.first()
            val translation=unique.drop(1)
            original.copy(translation=translation.joinToString("\n") {it.text},
                translationWords=translation.singleOrNull()?.words.orEmpty())
        }
        return grouped.mapIndexed {i,line ->line.copy(endMs=if(line.endMs>line.timeMs) line.endMs else grouped.getOrNull(i+1)?.timeMs ?: line.timeMs+5000)}
    }
    fun currentIndex(lines: List<LyricLine>,positionMs: Long): Int {
        var low=0;var high=lines.lastIndex;var answer=-1
        while(low<=high) {val mid=(low+high) ushr 1
            if(lines[mid].timeMs>=0 && lines[mid].timeMs<=positionMs) {answer=mid;low=mid+1} else high=mid-1
        }
        return answer
    }
}

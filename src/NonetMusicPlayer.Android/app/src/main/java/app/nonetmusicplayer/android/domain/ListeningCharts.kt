package app.nonetmusicplayer.android.domain

import java.time.*

data class ListeningBar(val label: String,val durationMs: Long)

/** 图表只做有依据的聚合；旧版每日总时长不会虚构到某个小时。 */
object ListeningCharts {
    fun days(rows: List<Listening>,today: LocalDate=LocalDate.now()): List<ListeningBar> {
        val totals=rows.groupBy {it.day}.mapValues {it.value.sumOf {r->r.durationMs}}
        return (6 downTo 0).map {val day=today.minusDays(it.toLong());ListeningBar("${day.monthValue}/${day.dayOfMonth}",totals[day.toString()] ?: 0)}
    }
    fun hours(rows: List<HourlyListening>,now: Instant=Instant.now(),zone: ZoneId=ZoneId.systemDefault()): List<ListeningBar> {
        val hour=Math.floorDiv(now.toEpochMilli(),3_600_000)
        val totals=rows.groupBy {it.utcHour}.mapValues {it.value.sumOf {row ->row.durationMs}}
        return (23 downTo 0).map {val key=hour-it;ListeningBar(Instant.ofEpochMilli(key*3_600_000).atZone(zone).hour.toString().padStart(2,'0'),totals[key] ?: 0)}
    }
    /** 将实际播放区间同时切分到小时与本地日期边界。 */
    fun split(endMs: Long,durationMs: Long,zone: ZoneId=ZoneId.systemDefault()): List<Triple<String,Long,Long>> {
        val result=mutableListOf<Triple<String,Long,Long>>()
        var start=endMs-durationMs.coerceAtLeast(0)
        while(start<endMs) {
            val date=Instant.ofEpochMilli(start).atZone(zone).toLocalDate()
            val hour=Math.floorDiv(start,3_600_000)
            val end=minOf(endMs,(hour+1)*3_600_000,date.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli())
            if(end<=start) break
            result+=Triple(date.toString(),hour,end-start);start=end
        }
        return result
    }
}

package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.*
import org.junit.Assert.*
import org.junit.Test
import java.time.*

class ListeningChartTests {
    @Test fun hourlyAndDailyUseTheSameDurationsAndHandleBoundaries() {
        val end=Instant.parse("2026-10-25T00:00:02Z").toEpochMilli()
        val parts=ListeningCharts.split(end,5000,ZoneId.of("UTC"))
        assertEquals(listOf("2026-10-24","2026-10-25"),parts.map {it.first})
        assertEquals(listOf(3000L,2000L),parts.map {it.third});assertEquals(5000L,parts.sumOf {it.third})
        val hours=ListeningCharts.hours(parts.map {HourlyListening(it.second,it.third,0)},Instant.ofEpochMilli(end),ZoneId.of("UTC"))
        assertEquals(24,hours.size);assertEquals(5000L,hours.sumOf {it.durationMs});assertEquals("00",hours.last().label)
    }
    @Test fun oldDailyTotalsArePreservedWithoutInventingHourlyHistory() {
        val days=ListeningCharts.days(listOf(Listening("a","2026-10-25",7_200_000,2)),LocalDate.of(2026,10,25))
        assertEquals(7,days.size);assertEquals(7_200_000L,days.last().durationMs)
        assertEquals(0L,ListeningCharts.hours(emptyList()).sumOf {it.durationMs})
        val zone=ZoneId.of("Asia/Kathmandu");val midnight=LocalDate.of(2026,10,25).atStartOfDay(zone).toInstant().toEpochMilli()
        assertEquals(listOf(500L,500L),ListeningCharts.split(midnight+500,1000,zone).map {it.third})
    }
}

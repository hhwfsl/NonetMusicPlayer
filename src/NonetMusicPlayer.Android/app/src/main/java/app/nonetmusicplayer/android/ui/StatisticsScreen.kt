package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import app.nonetmusicplayer.android.ui.musicString as stringResource
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.LibraryState
import java.time.*

@Composable fun StatisticsScreen(state: LibraryState) {
    var date by remember { mutableStateOf(LocalDate.now()) };var calendar by remember { mutableStateOf(false) }
    val stats=state.statistics
    LazyColumn(contentPadding=PaddingValues(20.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
        item { Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(12.dp)) {
            StatCell(stringResource(R.string.total_time),stringResource(R.string.hours,stats.sumOf { it.durationMs }/3600000.0),Modifier.weight(1f))
            StatCell(stringResource(R.string.total_plays),stats.sumOf { it.plays }.toString(),Modifier.weight(1f))
        } }
        item { Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(12.dp)) {
            StatCell(stringResource(R.string.tracks_heard),stats.map {it.trackId}.distinct().size.toString(),Modifier.weight(1f))
            StatCell(stringResource(R.string.active_days),stats.map {it.day}.distinct().size.toString(),Modifier.weight(1f))
        } }
        item { OutlinedButton(onClick={calendar=true}) { Icon(Icons.Outlined.CalendarMonth,null);Spacer(Modifier.width(8.dp));Text(date.toString()) } }
        for((prefix,label) in listOf(date.toString() to R.string.day_stats,date.toString().take(7) to R.string.month_stats,date.year.toString() to R.string.year_stats)) item {
            val period=stats.filter {it.day.startsWith(prefix)}
            val top=period.groupBy {it.trackId}.maxByOrNull {(_,list) -> list.sumOf {it.durationMs}}?.key
            Surface(shape=RoundedCornerShape(12.dp),color=NonetTheme.colorScheme.surfaceVariant) {
                Column(Modifier.fillMaxWidth().padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                    Text(stringResource(label)+" · "+prefix,style=NonetTheme.typography.titleLarge,fontWeight=FontWeight.SemiBold)
                    Text(stringResource(R.string.hours,period.sumOf {it.durationMs}/3600000.0),style=NonetTheme.typography.headlineSmall,color=NonetTheme.colorScheme.primary)
                    Text(stringResource(R.string.top_track),style=NonetTheme.typography.labelLarge,color=NonetTheme.colorScheme.onSurfaceVariant)
                    Text(state.tracks.firstOrNull {it.id==top}?.title ?: stringResource(R.string.no_statistics))
                    val mostPlayed=period.groupBy {it.trackId}.maxByOrNull {it.value.sumOf {row ->row.plays}}?.key
                    Text(stringResource(R.string.top_plays),style=NonetTheme.typography.labelLarge,color=NonetTheme.colorScheme.onSurfaceVariant)
                    Text(state.tracks.firstOrNull {it.id==mostPlayed}?.title ?: stringResource(R.string.no_statistics))
                    val trackMap=state.tracks.associateBy {it.id}
                    val artist=period.groupBy {trackMap[it.trackId]?.artist.orEmpty()}.filterKeys {it.isNotBlank()}.maxByOrNull {it.value.sumOf {row->row.durationMs}}?.key
                    Text(stringResource(R.string.top_artist),style=NonetTheme.typography.labelLarge,color=NonetTheme.colorScheme.onSurfaceVariant)
                    Text(artist ?: stringResource(R.string.no_statistics))
                }
            }
        }
    }
    if(calendar) {
        CalendarChooser(date,{calendar=false}) {date=it;calendar=false}
    }
}
@Composable private fun StatCell(label: String,value: String,modifier: Modifier) {
    Surface(modifier,shape=RoundedCornerShape(20.dp),color=NonetTheme.colorScheme.surfaceVariant) {
        Column(Modifier.padding(20.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Text(label,style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=2)
            Text(value,style=NonetTheme.typography.headlineSmall,fontWeight=FontWeight.SemiBold)
        }
    }
}

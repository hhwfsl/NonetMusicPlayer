package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import java.time.*
import java.time.format.TextStyle
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 紧凑日历与直接年份/月选择，不使用 Material DatePicker 或文本输入年份。 */
@Composable internal fun CalendarChooser(initial: LocalDate,onDismiss: ()->Unit,onSelect: (LocalDate)->Unit) {
    var selected by remember {mutableStateOf(initial)};var month by remember {mutableStateOf(YearMonth.from(initial))}
    var years by remember {mutableStateOf(false)};var months by remember {mutableStateOf(false)}
    val locale=(LocalMusicContext.current ?: androidx.compose.ui.platform.LocalContext.current).resources.configuration.locales[0]
    val range=(LocalDate.now().year-100)..(LocalDate.now().year+100)
    AlertDialog(onDismiss,{TextButton(onClick={onSelect(selected)}) {Text(stringResource(R.string.confirm))}},title={Text(selected.toString(),style=NonetTheme.typography.titleMedium)},text={
        Column(verticalArrangement=Arrangement.spacedBy(6.dp)) {
            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween) {
                TextButton(onClick={if(month>YearMonth.of(range.first,1)) month=month.minusMonths(1)}) {Text("‹")}
                Box {TextButton(onClick={years=true}) {Text(month.year.toString())};DropdownMenu(years,{years=false}) {range.forEach {y ->DropdownMenuItem({Text(y.toString())}) {month=month.withYear(y);years=false}}}}
                Box {TextButton(onClick={months=true}) {Text(month.month.getDisplayName(TextStyle.SHORT,locale))};DropdownMenu(months,{months=false}) {(1..12).forEach {m ->DropdownMenuItem({Text(Month.of(m).getDisplayName(TextStyle.FULL,locale))}) {month=month.withMonth(m);months=false}}}}
                TextButton(onClick={if(month<YearMonth.of(range.last,12)) month=month.plusMonths(1)}) {Text("›")}
            }
            Row {DayOfWeek.values().forEach {day ->Text(day.getDisplayName(TextStyle.NARROW,locale),Modifier.weight(1f),color=NonetTheme.colorScheme.onSurfaceVariant,textAlign=TextAlign.Center)}}
            val skip=month.atDay(1).dayOfWeek.value-1
            repeat(6) {row ->Row(Modifier.fillMaxWidth()) {repeat(7) {column ->val day=row*7+column-skip+1
                Box(Modifier.weight(1f)) {
                    if(day in 1..month.lengthOfMonth()) {val value=month.atDay(day);Box(Modifier.fillMaxWidth().height(44.dp).then(if(value==selected) Modifier.background(NonetTheme.colorScheme.primaryContainer,RoundedCornerShape(8.dp)) else Modifier).clickable {selected=value},contentAlignment=Alignment.Center) {Text(day.toString(),textAlign=TextAlign.Center)}}
                    else Spacer(Modifier.height(44.dp))
                }
            }}}
        }
    },dismissButton={TextButton(onClick=onDismiss) {Text(stringResource(R.string.cancel))}})
}

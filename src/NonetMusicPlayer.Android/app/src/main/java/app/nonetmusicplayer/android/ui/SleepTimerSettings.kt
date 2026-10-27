package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.Preferences
import app.nonetmusicplayer.android.playback.SleepTimer
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 仅保存默认选项；启动、取消和剩余时间由应用级计时器统一管理。 */
@Composable internal fun SleepTimerSettings(p: Preferences,onSave: (Preferences)->Unit,onInvalid: ()->Unit,timer: SleepTimer) {
    val state by timer.state.collectAsStateWithLifecycle()
    var minutes by remember(p.sleepMinutes) {mutableStateOf(p.sleepMinutes.toString())}
    var finishSong by remember(p.sleepFinishSong) {mutableStateOf(p.sleepFinishSong)}
    var focused by remember {mutableStateOf(false)}
    val focus=LocalFocusManager.current
    fun duration()=minutes.toIntOrNull()?.takeIf {it in 1..1440}
    fun saveDefaults(): Int? {
        val value=duration()
        if(value==null) onInvalid() else onSave(p.copy(sleepMinutes=value,sleepFinishSong=finishSong))
        return value
    }
    SettingsCard(stringResource(R.string.sleep_timer)) {
        ChoiceSetting(stringResource(R.string.sleep_duration),minutes,(listOf(15,30,45,60,90,120)+p.sleepMinutes).distinct().sorted().map {it.toString() to stringResource(R.string.sleep_minutes,it)}) {minutes=it;saveDefaults()}
        OutlinedTextField(minutes,{minutes=it.take(4)},Modifier.fillMaxWidth().testTag("sleep_minutes").onFocusChanged {state ->
            if(focused && !state.hasFocus) saveDefaults()
            focused=state.hasFocus
        },label={Text(stringResource(R.string.sleep_custom_minutes))},singleLine=true,keyboardOptions=KeyboardOptions(keyboardType=KeyboardType.Number,imeAction=ImeAction.Done),keyboardActions=KeyboardActions(onDone={focus.clearFocus()}))
        Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {Text(stringResource(R.string.sleep_finish_song),Modifier.weight(1f));Switch(finishSong,{finishSong=it;saveDefaults()},Modifier.testTag("sleep_finish_song"))}
        Text(stringResource(R.string.sleep_hint),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
        if(state.active) Text(if(state.waitingForSong) stringResource(R.string.sleep_waiting) else stringResource(R.string.sleep_remaining,(state.remainingSeconds/60).toInt(),(state.remainingSeconds%60).toInt()),modifier=Modifier.testTag("sleep_status"),color=NonetTheme.colorScheme.primary)
        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick={val selected=saveDefaults();focus.clearFocus();if(selected!=null) timer.start(selected,finishSong)},modifier=Modifier.weight(1f).testTag("sleep_start")) {Text(stringResource(if(state.active) R.string.sleep_restart else R.string.sleep_start))}
            if(state.active) TextButton(onClick={timer.cancel()},modifier=Modifier.weight(1f).testTag("sleep_cancel")) {Text(stringResource(R.string.cancel))}
        }
    }
}

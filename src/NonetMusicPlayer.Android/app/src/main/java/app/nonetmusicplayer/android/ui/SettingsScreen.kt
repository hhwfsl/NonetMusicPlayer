package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.HelpOutline
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import app.nonetmusicplayer.android.ui.musicString as stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.*
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.playback.PlayerViewModel
import androidx.media3.common.util.UnstableApi
import androidx.lifecycle.compose.collectAsStateWithLifecycle

@Composable internal fun SettingsCard(title: String,content: @Composable ColumnScope.()->Unit) {
    Surface(shape=RoundedCornerShape(12.dp),color=NonetTheme.colorScheme.surface,border=BorderStroke(1.dp,NonetTheme.colorScheme.outlineVariant)) {
        Column(Modifier.fillMaxWidth().padding(16.dp),verticalArrangement=Arrangement.spacedBy(12.dp)) {
            Text(title,style=NonetTheme.typography.titleMedium,fontWeight=FontWeight.SemiBold)
            content()
        }
    }
}
@Composable internal fun ChoiceSetting(label: String,value: String,choices: List<Pair<String,String>>,onChange: (String)->Unit) {
    var expanded by remember { mutableStateOf(false) }
    Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
        Text(label,Modifier.weight(1f),maxLines=2,overflow=TextOverflow.Ellipsis)
        Box { TextButton(onClick={expanded=true}) { Text(choices.firstOrNull { it.first==value }?.second.orEmpty(),maxLines=1,overflow=TextOverflow.Ellipsis);Icon(Icons.Outlined.ExpandMore,null) }
            DropdownMenu(expanded,onDismissRequest={expanded=false}) { choices.forEach { (key,title) -> DropdownMenuItem(text={Text(title)},onClick={expanded=false;onChange(key)}) } }
        }
    }
}
/** 文本值在完成输入或失焦时提交；无效值不改变已保存设置。 */
@Composable fun CommitField(label: String,value: String,onCommit: (String)->Unit,keyboard: KeyboardType=KeyboardType.Text) {
    var draft by remember(value) { mutableStateOf(value) };var focused by remember { mutableStateOf(false) }
    val manager=LocalFocusManager.current
    OutlinedTextField(draft,{draft=it},Modifier.fillMaxWidth().onFocusChanged { state ->
        if(focused && !state.isFocused && draft!=value) onCommit(draft)
        focused=state.isFocused
    },label={Text(label)},singleLine=true,shape=RoundedCornerShape(8.dp),keyboardOptions=KeyboardOptions(keyboardType=keyboard,imeAction=ImeAction.Done),
        keyboardActions=KeyboardActions(onDone={manager.clearFocus()}))
}
@UnstableApi
@Composable fun SettingsScreen(p: Preferences,onSave: (Preferences)->Unit,onInvalid: ()->Unit,onReset: ()->Unit,onBackground: ()->Unit,vm: PlayerViewModel) {
    var query by remember {mutableStateOf("")}
    var reset by remember { mutableStateOf(false) };var clearHistory by remember {mutableStateOf(false)};var guide by remember {mutableStateOf(false)}
    val context=androidx.compose.ui.platform.LocalContext.current
    fun matches(vararg labels: Int)=query.isBlank() || labels.any {context.getString(it).contains(query,true)}
    var restoreUri by remember {mutableStateOf<android.net.Uri?>(null)}
    val backup=androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.CreateDocument("application/zip")) {uri ->if(uri!=null) vm.backup(uri)}
    val restore=androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.OpenDocument()) {restoreUri=it}
    fun systemSettings(action: String) {runCatching {context.startActivity(android.content.Intent(action,android.net.Uri.parse("package:"+context.packageName)).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK))}.onFailure {onInvalid()}}
    LazyColumn(contentPadding=PaddingValues(20.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
        item {SearchField(query,{query=it},placeholder=stringResource(R.string.search_settings))}
        if(matches(R.string.appearance,R.string.theme,R.string.language,R.string.font_size,R.string.accent,R.string.background_image,R.string.background_opacity)) item { SettingsCard(stringResource(R.string.appearance)) {
            ChoiceSetting(stringResource(R.string.theme),p.theme,listOf("system" to stringResource(R.string.theme_system),"light" to stringResource(R.string.theme_light),"dark" to stringResource(R.string.theme_dark))) {onSave(p.copy(theme=it))}
            ChoiceSetting(stringResource(R.string.language),p.language,listOf("system" to stringResource(R.string.theme_system),"zh" to "中文","en" to "English","ja" to "日本語")) {onSave(p.copy(language=it))}
            Text(stringResource(R.string.accent),style=NonetTheme.typography.bodyMedium)
            Row(horizontalArrangement=Arrangement.spacedBy(12.dp)) { listOf("#A895FF","#79CDBE","#EF87A5","#72A8F0","#E9B15D").forEach { hex ->
                Box(Modifier.size(40.dp).clip(CircleShape).background(Color(android.graphics.Color.parseColor(hex))).clickable {onSave(p.copy(accent=hex))},contentAlignment=Alignment.Center) {
                    if(p.accent.equals(hex,true)) Icon(Icons.Outlined.Check,null,tint=Color(0xFF24212B))
                }
            } }
            OutlinedButton(onClick=onBackground,shape=RoundedCornerShape(8.dp)) {Icon(Icons.Outlined.Image,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.background_image))}
            if(p.background.isNotBlank()) {
                Text(stringResource(R.string.background_opacity))
                var opacity by remember(p.backgroundOpacity) {mutableFloatStateOf(p.backgroundOpacity)}
                RoundSlider(opacity,{opacity=it},onValueChangeFinished={onSave(p.copy(backgroundOpacity=opacity))})
                TextButton(onClick={onSave(p.copy(background=""))}) {Text(stringResource(R.string.remove_background))}
            }
            CommitField(stringResource(R.string.accent),p.accent,{if(Regex("#[0-9a-fA-F]{6}").matches(it)) onSave(p.copy(accent=it)) else onInvalid()})
            Text(stringResource(R.string.font_size))
            var fontScale by remember(p.fontScale) {mutableFloatStateOf(p.fontScale)}
            RoundSlider(fontScale,onValueChange={fontScale=it},onValueChangeFinished={onSave(p.copy(fontScale=fontScale))},valueRange=.85f..1.3f)
        } }
        if(matches(R.string.playback_settings,R.string.history_limit,R.string.clear_history,R.string.reset_settings)) item { SettingsCard(stringResource(R.string.playback_settings)) {
            CommitField(stringResource(R.string.history_limit),p.historyLimit.toString(),{raw -> val n=raw.toIntOrNull();if(n!=null && n in 1..10000) onSave(p.copy(historyLimit=n)) else onInvalid()},KeyboardType.Number)
            TextButton(onClick={clearHistory=true}) {Icon(Icons.Outlined.History,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.clear_history))}
            TextButton(onClick={reset=true}) { Icon(Icons.Outlined.Restore,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.reset_settings),maxLines=2) }
        } }
        if(matches(R.string.system_integration,R.string.battery_settings,R.string.notification_settings)) item { SettingsCard(stringResource(R.string.system_integration)) {
            Text(stringResource(R.string.background_hint),style=NonetTheme.typography.bodySmall)
            OutlinedButton(shape=RoundedCornerShape(8.dp),onClick={systemSettings(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS)}) {Text(stringResource(R.string.battery_settings))}
            OutlinedButton(shape=RoundedCornerShape(8.dp),onClick={runCatching {context.startActivity(android.content.Intent(android.provider.Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(android.provider.Settings.EXTRA_APP_PACKAGE,context.packageName).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK))}.onFailure {onInvalid()}}) {Text(stringResource(R.string.notification_settings))}
        } }
        if(matches(R.string.scan_settings,R.string.scan_minimum,R.string.scan_included,R.string.scan_excluded)) item {
            ScanSettings(p.scan,{onSave(p.copy(scan=it))},onInvalid,vm::addScanFolder)
        }
        if(matches(R.string.lan_title,R.string.lan_approval_wait)) item {SettingsCard(stringResource(R.string.lan_title)) {
            Text(stringResource(R.string.lan_approval_wait_hint),style=NonetTheme.typography.bodySmall)
            CommitField(stringResource(R.string.lan_approval_wait),p.lanApprovalWaitSeconds.toString(),{raw ->
                val n=raw.toIntOrNull();if(n!=null && n in 10..120) onSave(p.copy(lanApprovalWaitSeconds=n)) else onInvalid()
            },KeyboardType.Number)
        }}
        if(matches(R.string.backup,R.string.backup_export,R.string.backup_import)) item { SettingsCard(stringResource(R.string.backup)) {
            OutlinedButton(shape=RoundedCornerShape(8.dp),onClick={backup.launch("Nonet_"+java.time.LocalDate.now()+".zip")}) {Icon(Icons.Outlined.FileUpload,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.backup_export))}
            OutlinedButton(shape=RoundedCornerShape(8.dp),onClick={restore.launch(arrayOf("*/*"))}) {Icon(Icons.Outlined.FileDownload,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.backup_import))}
        } }
        if(matches(R.string.about,R.string.help,R.string.version,R.string.update_check,R.string.update_repository)) item { SettingsCard(stringResource(R.string.about)) {
            TextButton(onClick={guide=true}) {Icon(Icons.AutoMirrored.Outlined.HelpOutline,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.help))}
            Text("Nonet",style=NonetTheme.typography.titleMedium)
            Text(stringResource(R.string.version,BuildConfig.VERSION_NAME),color=NonetTheme.colorScheme.onSurfaceVariant)
            val update by vm.app.updates.state.collectAsStateWithLifecycle()
            OutlinedButton(onClick={if(update.active || update.phase in setOf(app.nonetmusicplayer.android.updates.UpdatePhase.Available,app.nonetmusicplayer.android.updates.UpdatePhase.Ready)) vm.app.updates.present() else vm.app.updates.check()},enabled=update.phase!=app.nonetmusicplayer.android.updates.UpdatePhase.Checking,modifier=Modifier.testTag("check_updates")) {
                Icon(Icons.Outlined.SystemUpdate,null);Spacer(Modifier.width(8.dp));Text(stringResource(if(update.active || update.phase in setOf(app.nonetmusicplayer.android.updates.UpdatePhase.Available,app.nonetmusicplayer.android.updates.UpdatePhase.Ready)) R.string.update_view else if(update.phase==app.nonetmusicplayer.android.updates.UpdatePhase.Checking) R.string.update_checking else R.string.update_check))
            }
            Text(stringResource(R.string.update_repository),style=NonetTheme.typography.titleSmall)
            Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                Image(androidx.compose.ui.res.painterResource(R.drawable.github_mark),null,Modifier.size(22.dp),colorFilter=androidx.compose.ui.graphics.ColorFilter.tint(NonetTheme.colorScheme.onSurface))
                Text("GitHub · hhwfsl/NonetMusicPlayer",Modifier.weight(1f).clickable {
                    runCatching {context.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW,android.net.Uri.parse(app.nonetmusicplayer.android.updates.NONET_REPOSITORY)).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK))}.onFailure {vm.app.messages.tryEmit(R.string.update_open_failed)}
                },color=NonetTheme.colorScheme.primary,maxLines=2,overflow=TextOverflow.Ellipsis)
            }
            Text(stringResource(R.string.privacy),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
        } }
    }
    if(guide) AlertDialog(onDismissRequest={guide=false},title={Text(stringResource(R.string.help))},text={Text(stringResource(R.string.user_guide_body),Modifier.heightIn(max=360.dp).verticalScroll(rememberScrollState()))},confirmButton={TextButton(onClick={guide=false}) {Text(stringResource(R.string.close))}})
    if(clearHistory) AlertDialog(onDismissRequest={clearHistory=false},title={Text(stringResource(R.string.clear_history))},text={Text(stringResource(R.string.clear_history_confirm))},
        confirmButton={TextButton(onClick={clearHistory=false;vm.launchOperation {vm.repository.clearHistory()}}) {Text(stringResource(R.string.confirm))}},
        dismissButton={TextButton(onClick={clearHistory=false}) {Text(stringResource(R.string.cancel))}})
    restoreUri?.let {uri ->AlertDialog(onDismissRequest={restoreUri=null},title={Text(stringResource(R.string.backup_import))},text={Text(stringResource(R.string.backup_confirm))},
        confirmButton={TextButton(onClick={restoreUri=null;vm.restoreBackup(uri)}) {Text(stringResource(R.string.confirm))}},
        dismissButton={TextButton(onClick={restoreUri=null}) {Text(stringResource(R.string.cancel))}})}
    if(reset) AlertDialog(onDismissRequest={reset=false},title={Text(stringResource(R.string.reset_settings))},text={Text(stringResource(R.string.reset_confirm))},
        confirmButton={TextButton(onClick={reset=false;onReset()}){Text(stringResource(R.string.confirm))}},dismissButton={TextButton(onClick={reset=false}){Text(stringResource(R.string.cancel))}})
}

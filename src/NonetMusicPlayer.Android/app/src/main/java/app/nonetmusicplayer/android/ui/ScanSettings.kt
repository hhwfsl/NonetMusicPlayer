package app.nonetmusicplayer.android.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.data.DeviceMusicScanner
import app.nonetmusicplayer.android.domain.ScanOptions
import app.nonetmusicplayer.android.ui.musicString as stringResource

@Composable internal fun ScanSettings(options: ScanOptions,onSave: (ScanOptions)->Unit,onInvalid: ()->Unit,onFolder: (Uri,Boolean)->Unit) {
    val includes=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) {uri ->if(uri!=null) {if(DeviceMusicScanner.location(uri)==null) onInvalid() else onFolder(uri,false)}}
    val excludes=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) {uri ->if(uri!=null) {if(DeviceMusicScanner.location(uri)==null) onInvalid() else onFolder(uri,true)}}
    SettingsCard(stringResource(R.string.scan_settings)) {
        val multiplier=if(options.durationUnit=="minutes") 60 else 1
        CommitField(stringResource(R.string.scan_minimum),(options.minimumSeconds.toDouble()/multiplier).let {if(it%1==0.0) it.toInt().toString() else it.toString()}, {raw ->
            val seconds=raw.toDoubleOrNull()?.times(multiplier)
            if(seconds!=null && seconds.isFinite() && seconds in 0.0..604800.0 && seconds%1==0.0) onSave(options.copy(minimumSeconds=seconds.toInt())) else onInvalid()
        },KeyboardType.Decimal)
        ChoiceSetting(stringResource(R.string.scan_unit),options.durationUnit,listOf("seconds" to stringResource(R.string.scan_seconds),"minutes" to stringResource(R.string.scan_minutes))) {onSave(options.copy(durationUnit=it))}
        Text(stringResource(R.string.scan_rules_hint),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
        @Composable fun Folders(excluded: Boolean) {
            val rows=if(excluded) options.excludedFolders else options.includedFolders
            Row(verticalAlignment=Alignment.CenterVertically) {
                Text(stringResource(if(excluded) R.string.scan_excluded else R.string.scan_included),Modifier.weight(1f))
                IconAction(Icons.Outlined.CreateNewFolder,stringResource(R.string.add_folder)) {if(excluded) excludes.launch(null) else includes.launch(null)}
            }
            if(rows.isEmpty()) Text(stringResource(if(excluded) R.string.scan_excluded_empty else R.string.scan_all),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            rows.forEach {value ->Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                Text(DeviceMusicScanner.label(value),Modifier.weight(1f),maxLines=2,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.bodySmall)
                IconAction(Icons.Outlined.Close,stringResource(R.string.remove)) {onSave(if(excluded) options.copy(excludedFolders=rows-value) else options.copy(includedFolders=rows-value))}
            }}
        }
        Folders(false);Folders(true)
    }
}

/** 确认只展示快照；回调之后才申请媒体权限或执行扫描。 */
@Composable internal fun ScanConfirmation(options: ScanOptions,onConfirm: ()->Unit,onCancel: ()->Unit,onSettings: ()->Unit) {
    AlertDialog(onDismissRequest=onCancel,title={Text(stringResource(R.string.scan_music))},text={Column(Modifier.heightIn(max=360.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
        Text(stringResource(R.string.scan_confirm_body))
        Text(stringResource(R.string.scan_threshold,options.minimumSeconds))
        Text(stringResource(R.string.scan_included)+"\n"+if(options.includedFolders.isEmpty()) stringResource(R.string.scan_all) else options.includedFolders.joinToString("\n",transform=DeviceMusicScanner::label))
        if(options.excludedFolders.isNotEmpty()) Text(stringResource(R.string.scan_excluded)+"\n"+options.excludedFolders.joinToString("\n",transform=DeviceMusicScanner::label))
        TextButton(onClick=onSettings) {Text(stringResource(R.string.scan_settings))}
    }},confirmButton={TextButton(onClick=onConfirm,modifier=Modifier.testTag("scan_confirm")) {Text(stringResource(R.string.scan_start))}},
        dismissButton={TextButton(onClick=onCancel,modifier=Modifier.testTag("scan_cancel")) {Text(stringResource(R.string.cancel))}})
}

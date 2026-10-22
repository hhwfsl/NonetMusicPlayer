package app.nonetmusicplayer.android.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.LocalActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.compose.currentStateAsState
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.core.app.NotificationManagerCompat
import app.nonetmusicplayer.android.*
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.updates.*
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 纯展示同一个更新会话；收起弹窗不会取消服务，重新打开仍显示真实进度。 */
@androidx.annotation.OptIn(markerClass=[androidx.media3.common.util.UnstableApi::class])
@Composable internal fun UpdateDialog(app: MusicApplication) {
    val updates=app.updates;val state by updates.state.collectAsStateWithLifecycle()
    val lifecycle by LocalLifecycleOwner.current.lifecycle.currentStateAsState()
    val activity=LocalActivity.current as? MainActivity
    var requestedBackground by remember {mutableStateOf(false)}
    val permission=rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {granted ->
        if(!granted) app.messages.tryEmit(R.string.update_notification_permission)
        updates.begin(requestedBackground && granted)
    }
    fun download(background: Boolean) {
        requestedBackground=background
        if(Build.VERSION.SDK_INT>=33 && !NotificationManagerCompat.from(app).areNotificationsEnabled()) permission.launch(Manifest.permission.POST_NOTIFICATIONS)
        else {if(background && !NotificationManagerCompat.from(app).areNotificationsEnabled()) app.messages.tryEmit(R.string.update_notification_permission);updates.begin(background && NotificationManagerCompat.from(app).areNotificationsEnabled())}
    }
    LaunchedEffect(state.phase,state.install,lifecycle) {
        // 后台不抢占其他应用；恢复前台或点击安装通知后再请求系统安装器。
        if(state.phase==UpdatePhase.Ready && state.install && lifecycle==Lifecycle.State.RESUMED) activity?.installDownloadedUpdate()
    }
    if(!state.dialog) return
    Dialog(onDismissRequest={if(state.active) updates.background() else updates.dismiss()},properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.widthIn(max=560.dp).fillMaxWidth(.92f).testTag("update_dialog"),RoundedCornerShape(18.dp),border=BorderStroke(1.dp,NonetTheme.colorScheme.outlineVariant)) {
            Column(Modifier.padding(20.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
                Text(stringResource(R.string.update_title),style=NonetTheme.typography.titleLarge)
                state.release?.let {Text("Nonet ${it.version}",style=NonetTheme.typography.titleMedium)}
                when(state.phase) {
                    UpdatePhase.Available -> {
                        SelectionContainer {Text(state.release?.notes?.ifBlank {stringResource(R.string.update_no_notes)} ?: stringResource(R.string.update_no_notes),Modifier.heightIn(max=320.dp).verticalScroll(rememberScrollState()).testTag("update_notes"))}
                        Text(stringResource(R.string.update_install_hint),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                        Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick={download(true)},modifier=Modifier.weight(1f)) {Text(stringResource(R.string.update_download_background))}
                            Button(onClick={download(false)},modifier=Modifier.weight(1f).testTag("update_download")) {Text(stringResource(R.string.update_download))}
                        }
                        TextButton(onClick=updates::dismiss) {Text(stringResource(R.string.update_later))}
                    }
                    UpdatePhase.Downloading,UpdatePhase.Verifying,UpdatePhase.Cancelling -> {
                        val total=state.release?.size ?: 1L
                        Text(when(state.phase) {UpdatePhase.Verifying ->stringResource(R.string.update_verifying);UpdatePhase.Cancelling ->stringResource(R.string.update_cancelling);else ->stringResource(R.string.update_progress,(state.received*100/total).toInt())})
                        LinearProgressIndicator(progress=if(state.phase==UpdatePhase.Downloading) {{state.received.toFloat()/total}} else null,modifier=Modifier.testTag("update_progress"))
                        if(state.phase!=UpdatePhase.Cancelling) Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                            TextButton(onClick=updates::background,modifier=Modifier.testTag("update_background")) {Text(stringResource(R.string.update_to_background))}
                            TextButton(onClick={updates.cancel()},modifier=Modifier.testTag("update_cancel")) {Text(stringResource(R.string.update_cancel_download))}
                        }
                    }
                    UpdatePhase.Ready -> {
                        Text(stringResource(R.string.update_ready))
                        if(state.error!=0) Text(stringResource(state.error),color=NonetTheme.colorScheme.onSurfaceVariant)
                        Row {TextButton(onClick={updates.cancel()}) {Text(stringResource(R.string.cancel))};Button(onClick=updates::requestInstall) {Text(stringResource(R.string.update_install))}}
                    }
                    UpdatePhase.Checking ->Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(12.dp)) {CircularProgressIndicator();Text(stringResource(R.string.update_checking))}
                    UpdatePhase.Error -> {Text(stringResource(state.error));Button(onClick={updates.check()}) {Text(stringResource(R.string.update_retry))}}
                    else ->Text(stringResource(R.string.update_current))
                }
                if(state.phase in setOf(UpdatePhase.Current,UpdatePhase.Error,UpdatePhase.Checking,UpdatePhase.Ready)) TextButton(onClick=updates::dismiss) {Text(stringResource(R.string.close))}
            }
        }
    }
}

package app.nonetmusicplayer.android.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.core.content.edit
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.playback.PlayerViewModel
import app.nonetmusicplayer.android.network.PeerBackgroundAccess
import app.nonetmusicplayer.android.ui.musicString as stringResource

@UnstableApi
@Composable fun NearbyScreen(vm: PlayerViewModel) {
    val state by vm.app.lan.state.collectAsStateWithLifecycle();val context=LocalContext.current
    var invitation by remember {mutableStateOf("")}
    var backgroundAllowed by remember {mutableStateOf(PeerBackgroundAccess.allowed(context))}
    val backgroundLauncher=rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) {backgroundAllowed=PeerBackgroundAccess.allowed(context)}
    val lifecycle=LocalLifecycleOwner.current.lifecycle
    DisposableEffect(lifecycle,context) {
        val observer=LifecycleEventObserver {_,event ->if(event==Lifecycle.Event.ON_RESUME) backgroundAllowed=PeerBackgroundAccess.allowed(context)}
        lifecycle.addObserver(observer);onDispose {lifecycle.removeObserver(observer)}
    }
    val permissionHistory=remember {context.getSharedPreferences("permissions",Context.MODE_PRIVATE)}
    fun backgroundSettings() {
        runCatching {backgroundLauncher.launch(PeerBackgroundAccess.settingsIntent(context))}.onFailure {
            vm.app.log.write("WARN","LAN background settings unavailable",it)
        }
    }
    fun offerBackgroundAccess() {
        // 首次主动共享时交给系统确认；拒绝不会阻止连接，也不在每次连接时重复请求。
        if(!PeerBackgroundAccess.allowed(context) && !permissionHistory.getBoolean("lanBackgroundRequested",false)) {
            permissionHistory.edit {putBoolean("lanBackgroundRequested",true)};backgroundSettings()
        }
    }
    LazyColumn(contentPadding=PaddingValues(16.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
        item {SettingsCard(stringResource(R.string.lan_share)) {
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                Text(stringResource(R.string.lan_share),Modifier.weight(1f));Switch(state.sharing,{vm.app.lan.setSharing(it);if(it) offerBackgroundAccess()})
            }
            Text(stringResource(R.string.lan_privacy),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            state.invitations.forEach {text ->OutlinedButton(onClick={context.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("Nonet",text))}) {
                Icon(Icons.Outlined.ContentCopy,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.lan_copy)+" · "+runCatching {java.net.URI(text).rawQuery.substringAfter("host=").substringBefore('&')}.getOrDefault(""))
            }}
            if(state.sharing) TextButton(onClick={vm.app.lan.renew()}) {Text(stringResource(R.string.lan_renew))}
        }}
        item {SettingsCard(stringResource(R.string.lan_connect)) {
            Text(stringResource(R.string.lan_invitation_hint),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            OutlinedTextField(invitation,{invitation=it.take(2048)},Modifier.fillMaxWidth(),label={Text(stringResource(R.string.lan_invitation))},shape=RoundedCornerShape(10.dp),maxLines=3)
            Button(onClick={val text=invitation;invitation="";vm.app.lan.connect(text);offerBackgroundAccess()},enabled=invitation.isNotBlank() && !state.connecting) {
                if(state.connecting) CircularProgressIndicator(Modifier.size(18.dp),strokeWidth=2.dp) else Icon(Icons.Outlined.Link,null)
                Spacer(Modifier.width(8.dp));Text(stringResource(R.string.lan_connect))
            }
            if(state.connecting) Text(stringResource(R.string.lan_waiting),style=NonetTheme.typography.bodySmall)
        }}
        if(state.peers.isEmpty()) item {EmptyState(stringResource(R.string.lan_empty))}
        items(state.peers,key={it.id}) {peer ->SettingsCard(peer.name) {
            Text(stringResource(if(peer.reachable) R.string.lan_available else R.string.lan_reconnecting),style=NonetTheme.typography.bodySmall)
            TextButton(onClick={vm.app.lan.disconnect(peer.id)}) {Text(stringResource(R.string.lan_disconnect))}
        }}
        item {SettingsCard(stringResource(R.string.lan_background_settings)) {
            Text(stringResource(if(backgroundAllowed) R.string.lan_background_allowed else R.string.lan_background_limited),Modifier.testTag("lan_background_status"))
            Text(stringResource(R.string.lan_background_hint),style=NonetTheme.typography.bodySmall)
            TextButton(onClick={backgroundSettings()},modifier=Modifier.testTag("lan_background_settings")) {Text(stringResource(if(backgroundAllowed) R.string.lan_background_settings else R.string.lan_background_allow))}
        }}
    }
}

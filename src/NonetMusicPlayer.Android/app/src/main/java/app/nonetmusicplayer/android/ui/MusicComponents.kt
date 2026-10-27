package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import app.nonetmusicplayer.android.ui.musicString as stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.focus.focusRequester
import coil.compose.SubcomposeAsyncImage
import androidx.compose.ui.layout.ContentScale
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.Track
import java.io.File

@Composable fun IconAction(icon: ImageVector, label: String, modifier: Modifier = Modifier, enabled: Boolean = true, tint: Color=NonetTheme.colorScheme.onSurfaceVariant, onClick: () -> Unit) {
    IconButton(onClick=onClick,enabled=enabled,modifier=modifier.size(48.dp)) { Icon(icon,label,tint=if(enabled) tint else tint.copy(alpha=.4f)) }
}
@Composable fun Cover(path: String, modifier: Modifier=Modifier, liked: Boolean=false) {
    val shape=RoundedCornerShape(10.dp)
    val colors=NonetTheme.colorScheme
    @Composable fun Placeholder() { Box(Modifier.fillMaxSize().background(Brush.linearGradient(listOf(colors.surfaceVariant,colors.primaryContainer))),contentAlignment=Alignment.Center) {
        Icon(if(liked) Icons.Outlined.Favorite else Icons.Outlined.MusicNote, null,tint=if(liked) Color(0xFFEF536D) else colors.onSurfaceVariant,modifier=Modifier.fillMaxSize(.36f))
    } }
    Box(modifier.clip(shape)) {
        if(path.isBlank()) Placeholder() else SubcomposeAsyncImage(model=if(path.startsWith("https:")) path else File(path),contentDescription=null,
            modifier=Modifier.fillMaxSize(),contentScale=ContentScale.Crop,loading={ Placeholder() },error={ Placeholder() })
    }
}
@Composable fun SearchField(value: String, onSearch: (String)->Unit, modifier: Modifier=Modifier,placeholder: String=stringResource(R.string.search),requestFocus: Boolean=false,onFocusHandled: ()->Unit={}) {
    var draft by remember(value) { mutableStateOf(value) }
    val focus=LocalFocusManager.current
    var focused by remember {mutableStateOf(false)}
    val requester=remember {androidx.compose.ui.focus.FocusRequester()}
    val keyboard=androidx.compose.ui.platform.LocalSoftwareKeyboardController.current
    val window=androidx.compose.ui.platform.LocalWindowInfo.current
    // 请求只消费一次，不在返回页面、切前后台或普通重组时反复抢占焦点。
    LaunchedEffect(requestFocus,window.isWindowFocused) {
        if(requestFocus && window.isWindowFocused) {
            withFrameNanos {}
            if(requester.requestFocus(androidx.compose.ui.focus.FocusDirection.Enter)) {keyboard?.show();onFocusHandled()}
        }
    }
    Row(modifier.height(44.dp).clip(RoundedCornerShape(10.dp)).background(NonetTheme.colorScheme.surface).border(1.dp,NonetTheme.colorScheme.outlineVariant,RoundedCornerShape(10.dp)),verticalAlignment=Alignment.CenterVertically) {
        IconAction(Icons.Outlined.Search,stringResource(R.string.search)) {onSearch(draft);focus.clearFocus()}
        BasicTextField(draft,{draft=it.take(300)},Modifier.weight(1f).focusRequester(requester).testTag("search_input").onFocusChanged {if(focused && !it.isFocused && draft!=value) onSearch(draft);focused=it.isFocused},singleLine=true,
            textStyle=NonetTheme.typography.bodyMedium.copy(color=NonetTheme.colorScheme.onSurface),cursorBrush=androidx.compose.ui.graphics.SolidColor(NonetTheme.colorScheme.primary),
            keyboardOptions=KeyboardOptions(imeAction=ImeAction.Search),keyboardActions=KeyboardActions(onSearch={onSearch(draft);focus.clearFocus()}),
            decorationBox={inner ->Box {if(draft.isBlank()) Text(placeholder,color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=1,overflow=TextOverflow.Ellipsis);inner()}})
        if(draft.isNotEmpty()) IconAction(Icons.Outlined.Close,stringResource(R.string.clear)) {draft="";onSearch("");focus.clearFocus()}
    }
}
@Composable fun SectionHeading(title: String, onAll: (() -> Unit)?=null) {
    Row(Modifier.fillMaxWidth().padding(top=12.dp,bottom=8.dp),verticalAlignment=Alignment.CenterVertically) {
        Text(title,style=NonetTheme.typography.titleLarge,fontWeight=FontWeight.SemiBold,modifier=Modifier.weight(1f),maxLines=1,overflow=TextOverflow.Ellipsis)
        if(onAll!=null) TextButton(onClick=onAll) { Text(stringResource(R.string.view_all),maxLines=1,overflow=TextOverflow.Ellipsis);Icon(Icons.Outlined.ChevronRight,null,Modifier.size(18.dp)) }
    }
}
@Composable fun EmptyState(title: String, body: String="", action: String="", onAction: () -> Unit={}) {
    Column(Modifier.fillMaxWidth().padding(vertical=40.dp,horizontal=24.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(12.dp)) {
        Cover("",Modifier.size(84.dp))
        Text(title,style=NonetTheme.typography.titleMedium,fontWeight=FontWeight.SemiBold)
        if(body.isNotBlank()) Text(body,style=NonetTheme.typography.bodyMedium,color=NonetTheme.colorScheme.onSurfaceVariant)
        if(action.isNotBlank()) OutlinedButton(onClick=onAction) { Icon(Icons.Outlined.Add,null);Spacer(Modifier.width(8.dp));Text(action) }
    }
}
@Composable fun TrackRow(track: Track, index: Int, active: Boolean, liked: Boolean, onPlay: () -> Unit,onLike: () -> Unit,onMore: () -> Unit) {
    Surface(onClick=onPlay,modifier=Modifier.testTag("track_${track.id}"),shape=RoundedCornerShape(10.dp),color=if(active) NonetTheme.colorScheme.primaryContainer.copy(alpha=.55f) else Color.Transparent) {
        Row(Modifier.fillMaxWidth().padding(start=4.dp,top=5.dp,bottom=5.dp),verticalAlignment=Alignment.CenterVertically) {
            Text((index+1).toString(),Modifier.width(26.dp),style=NonetTheme.typography.labelSmall,color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=1)
            Cover(track.cover,Modifier.size(42.dp));Spacer(Modifier.width(10.dp))
            Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(3.dp)) {
                Text(track.title,maxLines=1,overflow=TextOverflow.Ellipsis,fontWeight=if(active) FontWeight.SemiBold else FontWeight.Normal,color=if(active) NonetTheme.colorScheme.primary else NonetTheme.colorScheme.onSurface)
                Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(5.dp)) {
                    Text(track.artist.ifBlank {stringResource(R.string.unknown_artist)},Modifier.weight(1f),maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                    if(track.providerId.isNotBlank()) Text(stringResource(if(track.providerId.startsWith("lan:")) when(track.sourceDeviceKind) {"desktop"->R.string.lan_source_desktop;"mobile"->R.string.lan_source_mobile;else->R.string.lan_source_unknown} else R.string.lan_source_unknown),style=NonetTheme.typography.labelSmall,color=NonetTheme.colorScheme.primary)
                    Text(timeLabel(track.durationMs),style=NonetTheme.typography.labelSmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                }
            }
            IconAction(if(liked) Icons.Outlined.Favorite else Icons.Outlined.FavoriteBorder,stringResource(if(liked) R.string.remove_liked else R.string.add_liked),tint=if(liked) Color(0xFFEF536D) else NonetTheme.colorScheme.onSurfaceVariant,onClick=onLike)
            IconAction(Icons.Outlined.MoreVert,stringResource(R.string.more),onClick=onMore)
        }
    }
}
fun timeLabel(ms: Long): String = "%d:%02d".format(ms.coerceAtLeast(0)/60000,ms.coerceAtLeast(0)/1000%60)

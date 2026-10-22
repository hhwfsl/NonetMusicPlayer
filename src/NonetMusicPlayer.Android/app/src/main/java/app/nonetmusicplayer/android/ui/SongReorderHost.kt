package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.scrollBy
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.domain.Track
import kotlin.math.abs
import kotlin.math.roundToInt

/** 整行长按拖动：手势由列表外层持有，原行滚出虚拟视口后仍能边缘滚动，松手才写入一次排序。 */
@Composable internal fun SongReorderHost(tracks: List<Track>,enabled: Boolean,onMove: (Track,Int)->Unit,
    draggedContent: @Composable (Track)->Unit,content: @Composable (LazyListState,String?)->Unit) {
    val list=rememberLazyListState()
    val currentTracks by rememberUpdatedState(tracks);val move by rememberUpdatedState(onMove)
    val currentIndex by rememberUpdatedState(remember(tracks) {tracks.withIndex().associate {it.value.id to it.index}})
    var draggedId by remember {mutableStateOf<String?>(null)}
    var pointer by remember {mutableFloatStateOf(0f)};var grab by remember {mutableFloatStateOf(0f)}
    var destination by remember {mutableIntStateOf(-1)}
    val edge=with(LocalDensity.current) {48.dp.toPx()};val speed=with(LocalDensity.current) {12.dp.toPx()}
    fun nearest(): Int {
        val info=list.layoutInfo
        val keys=currentIndex
        val target=info.visibleItemsInfo.filter {it.key in keys}.minByOrNull {abs(it.offset+info.beforeContentPadding+it.size/2f-pointer)}
        return target?.let {keys[it.key]} ?: -1
    }
    val scrollStep by remember(list,edge,speed) {derivedStateOf {
        if(draggedId==null) 0f else {
            val height=list.layoutInfo.viewportSize.height
            when {pointer<edge -> -speed*((edge-pointer)/edge).coerceIn(0f,1f);pointer>height-edge ->speed*((pointer-height+edge)/edge).coerceIn(0f,1f);else->0f}
        }
    }}
    LaunchedEffect(draggedId,enabled,scrollStep) {
        if(!enabled) {draggedId=null;return@LaunchedEffect}
        if(scrollStep==0f) return@LaunchedEffect
        while(draggedId!=null) {
            withFrameNanos { }
            list.scrollBy(scrollStep);destination=nearest()
        }
    }
    val gesture=if(enabled) Modifier.pointerInput(list,enabled) {
        detectDragGesturesAfterLongPress(onDragStart={position ->
            val info=list.layoutInfo
            val row=info.visibleItemsInfo.firstOrNull {position.y>=it.offset+info.beforeContentPadding && position.y<it.offset+info.beforeContentPadding+it.size && it.key in currentIndex}
            if(row!=null) {pointer=position.y;grab=position.y-row.offset-info.beforeContentPadding;draggedId=row.key as String;destination=currentTracks.indexOfFirst {it.id==row.key}}
        },onDrag={change,amount ->if(draggedId!=null) {change.consume();pointer+=amount.y;destination=nearest()}},
            onDragCancel={draggedId=null},onDragEnd={
                val track=currentTracks.firstOrNull {it.id==draggedId};val start=currentTracks.indexOfFirst {it.id==draggedId};val end=destination
                draggedId=null
                if(track!=null && start>=0 && end>=0 && end!=start) move(track,end-start)
            })
    } else Modifier
    Box(Modifier.fillMaxSize().clipToBounds().then(gesture).testTag("song_reorder_host")) {
        content(list,draggedId)
        currentTracks.firstOrNull {it.id==draggedId}?.let {track ->
            Box(Modifier.fillMaxWidth().offset {IntOffset(0,(pointer-grab).roundToInt())}.padding(horizontal=16.dp)
                .background(NonetTheme.colorScheme.primaryContainer,RoundedCornerShape(10.dp)).testTag("song_drag_preview")) {draggedContent(track)}
        }
    }
}

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
import kotlinx.coroutines.delay
import kotlin.math.abs
import kotlin.math.roundToInt

/** 队列、歌单内容与侧栏共用预览排序；拖动期间只改显示快照，松手提交一次，取消不写入。 */
@Composable internal fun <T> ReorderableListHost(entries: List<T>,key: (T)->String,enabled: Boolean,onMove: (T,Int)->Unit,
    modifier: Modifier=Modifier,list: LazyListState=rememberLazyListState(),tag: String="song_reorder_host",
    draggedContent: @Composable (T)->Unit,content: @Composable (LazyListState,List<T>,String?)->Unit) {
    val keys=remember(entries) {entries.map(key)}
    val currentEntries by rememberUpdatedState(entries);val currentKey by rememberUpdatedState(key)
    val move by rememberUpdatedState(onMove)
    var preview by remember {mutableStateOf<List<String>?>(null)}
    var draggedId by remember {mutableStateOf<String?>(null)}
    var pointer by remember {mutableFloatStateOf(0f)};var grab by remember {mutableFloatStateOf(0f)}
    var rowHeight by remember {mutableFloatStateOf(0f)}
    var contentInset by remember {mutableIntStateOf(0)}
    var hasMoved by remember {mutableStateOf(false)}
    val byId=remember(entries) {entries.associateBy(key)}
    val displayed=remember(preview,byId,entries) {preview?.mapNotNull {byId[it]} ?: entries}
    val previewIndices=remember(preview) {preview.orEmpty().withIndex().associate {it.value to it.index}}
    val currentIndices by rememberUpdatedState(previewIndices)
    // 数据增删或外部排序时撤销过期预览；统计和封面刷新不打断正在进行的手势。
    LaunchedEffect(keys,enabled) {preview=null;draggedId=null}
    LaunchedEffect(draggedId,preview) {
        // 保存失败或控制器断开时最终回到真实数据，避免长期展示未被服务接受的顺序。
        if(draggedId==null && preview!=null) {delay(3000);preview=null}
    }
    fun updatePreview() {
        val id=draggedId ?: return
        val order=preview ?: return
        val indices=currentIndices
        val info=list.layoutInfo
        val rows=info.visibleItemsInfo.filter {it.key in indices}
        // 等待上一轮换位完成布局，避免用旧坐标把刚让位的歌曲再次交换回去。
        if(rows.map {it.index-indices.getValue(it.key as String)}.distinct().size>1) return
        val center=pointer-grab+rowHeight/2
        val nearest=rows.minByOrNull {abs(it.offset+info.beforeContentPadding+it.size/2f-center)} ?: return
        val from=order.indexOf(id);val to=indices[nearest.key] ?: return
        if(from>=0 && from!=to) {
            // 可见首项也可能正在拖动，按索引保留视口，避免稳定 key 锚定该项造成整页跳动。
            list.requestScrollToItem(list.firstVisibleItemIndex,list.firstVisibleItemScrollOffset)
            preview=order.toMutableList().apply {add(to,removeAt(from))}
        }
    }
    val edge=with(LocalDensity.current) {48.dp.toPx()};val speed=with(LocalDensity.current) {12.dp.toPx()}
    LaunchedEffect(draggedId) {
        if(draggedId==null) return@LaunchedEffect
        while(draggedId!=null) {
            // 边缘滚动由协程调度，不让静止手指持续请求界面帧；无位置变化时不触发重组。
            delay(16)
            val height=list.layoutInfo.viewportSize.height
            val step=when {pointer<edge -> -speed*((edge-pointer)/edge).coerceIn(0f,1f)
                pointer>height-edge ->speed*((pointer-height+edge)/edge).coerceIn(0f,1f);else->0f}
            if(hasMoved && step!=0f && list.scrollBy(step)!=0f) updatePreview()
        }
    }
    val gesture=if(enabled) Modifier.pointerInput(list,enabled) {
        detectDragGesturesAfterLongPress(onDragStart={position ->
            val available=currentEntries.map(currentKey).toSet();val info=list.layoutInfo
            val row=info.visibleItemsInfo.firstOrNull {position.y>=it.offset+info.beforeContentPadding && position.y<it.offset+info.beforeContentPadding+it.size && it.key in available}
            if(row!=null) {
                preview=preview ?: currentEntries.map(currentKey)
                pointer=position.y;grab=position.y-row.offset-info.beforeContentPadding;rowHeight=row.size.toFloat()
                contentInset=info.beforeContentPadding;hasMoved=false;draggedId=row.key as String
            }
        },onDrag={change,amount ->if(draggedId!=null) {change.consume();if(amount.y!=0f) hasMoved=true;pointer+=amount.y;updatePreview()}},
            onDragCancel={draggedId=null;preview=null},onDragEnd={
                val entry=currentEntries.firstOrNull {currentKey(it)==draggedId}
                val from=currentEntries.indexOfFirst {currentKey(it)==draggedId};val to=preview?.indexOf(draggedId) ?: -1
                draggedId=null
                if(entry!=null && from>=0 && to>=0 && from!=to) move(entry,to-from) else preview=null
                // 异步保存期间保留最终预览，直到新快照到达，避免松手时先跳回旧顺序。
            })
    } else Modifier
    Box(modifier.fillMaxSize().clipToBounds().then(gesture).testTag(tag)) {
        content(list,displayed,draggedId)
        entries.firstOrNull {key(it)==draggedId}?.let {entry ->
            Box(Modifier.fillMaxWidth().offset {IntOffset(0,(pointer-grab).roundToInt())}
                // 只使用拾起时的内边距，不在组合阶段观察每次布局都会变化的 layoutInfo。
                .padding(horizontal=with(LocalDensity.current) {contentInset.toDp()})
                .background(NonetTheme.colorScheme.primaryContainer,RoundedCornerShape(10.dp)).testTag("${tag}_preview")) {draggedContent(entry)}
        }
    }
}
/** 歌曲保持稳定 ID，原行变为占位，周围行通过 animateItem 连贯让位。 */
@Composable internal fun SongReorderHost(tracks: List<Track>,enabled: Boolean,onMove: (Track,Int)->Unit,
    modifier: Modifier=Modifier,tag: String="song_reorder_host",
    draggedContent: @Composable (Track)->Unit,content: @Composable (LazyListState,List<Track>,String?)->Unit) {
    ReorderableListHost(tracks,{it.id},enabled,onMove,modifier,tag=tag,draggedContent=draggedContent,content=content)
}

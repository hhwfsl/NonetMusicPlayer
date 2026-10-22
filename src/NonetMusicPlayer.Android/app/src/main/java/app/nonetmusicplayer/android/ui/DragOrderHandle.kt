package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt

/** 歌单卡片的更多按钮支持长按排序；歌曲列表使用独立的整行拖拽宿主。 */
@Composable fun Modifier.reorderOnLongPress(onMove: (Int)->Unit): Modifier {
    val move by rememberUpdatedState(onMove);val unit=with(LocalDensity.current){64.dp.toPx()}
    var distance by remember {mutableFloatStateOf(0f)}
    return this.pointerInput(unit) {
            detectDragGesturesAfterLongPress(onDragStart={distance=0f},
                onDrag={change,amount ->change.consume();distance+=amount.y},
                onDragCancel={distance=0f},onDragEnd={val delta=(distance/unit).roundToInt();if(delta!=0) move(delta);distance=0f})
        }
}

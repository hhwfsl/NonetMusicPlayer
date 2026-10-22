package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.*
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.semantics.*
import androidx.compose.ui.unit.dp

/** 连续细轨道和单个圆柄；没有 Material 轨道间隙、端点圆点或额外白条。 */
@Composable fun RoundSlider(value: Float, onValueChange: (Float)->Unit,
    modifier: Modifier=Modifier, onValueChangeFinished: ()->Unit={},
    valueRange: ClosedFloatingPointRange<Float> = 0f..1f) {
    val current by rememberUpdatedState(value);val update by rememberUpdatedState(onValueChange);val finish by rememberUpdatedState(onValueChangeFinished)
    val density=androidx.compose.ui.platform.LocalDensity.current;val radius=with(density) {7.dp.toPx()}
    var width by remember {mutableIntStateOf(0)};var dragged by remember {mutableFloatStateOf(value)}
    val span=(valueRange.endInclusive-valueRange.start).coerceAtLeast(.0001f)
    fun fromX(x: Float)=(valueRange.start+(x-radius)/(width-2*radius).coerceAtLeast(1f)*span).coerceIn(valueRange)
    val colors=NonetTheme.colorScheme
    Canvas(modifier.fillMaxWidth().height(44.dp).onSizeChanged {width=it.width}
        .semantics {progressBarRangeInfo=ProgressBarRangeInfo(current.coerceIn(valueRange),valueRange);setProgress {update(it.coerceIn(valueRange));finish();true}}
        .pointerInput(valueRange,width) {detectTapGestures {update(fromX(it.x));finish()}}
        .draggable(rememberDraggableState {delta ->dragged=(dragged+delta/(width-2*radius).coerceAtLeast(1f)*span).coerceIn(valueRange);update(dragged)},Orientation.Horizontal,
            onDragStarted={dragged=fromX(it.x);update(dragged)},onDragStopped={finish()})) {
        val x=radius+(size.width-2*radius)*((current-valueRange.start)/span).coerceIn(0f,1f)
        drawLine(colors.outlineVariant,Offset(radius,size.height/2),Offset(size.width-radius,size.height/2),3.dp.toPx(),StrokeCap.Round)
        drawLine(colors.primary,Offset(radius,size.height/2),Offset(x,size.height/2),3.dp.toPx(),StrokeCap.Round)
        drawCircle(colors.primary,radius,Offset(x,size.height/2))
    }
}

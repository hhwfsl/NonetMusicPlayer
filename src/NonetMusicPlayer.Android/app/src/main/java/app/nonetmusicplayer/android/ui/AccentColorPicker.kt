package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Palette
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.AccentColor
import app.nonetmusicplayer.android.ui.musicString as stringResource
import kotlin.math.*

@Composable internal fun AccentColorSetting(value: String,onSave: (String)->Unit) {
    var open by remember {mutableStateOf(false)}
    Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(10.dp)) {
        Box(Modifier.size(32.dp).background(Color((AccentColor.parse(value) ?: 0xFFA895FF).toInt()),CircleShape).border(1.dp,NonetTheme.colorScheme.outlineVariant,CircleShape))
        Column(Modifier.weight(1f)) {Text(stringResource(R.string.accent));Text(value,style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)}
        OutlinedButton(onClick={open=true},modifier=Modifier.testTag("accent_picker_open")) {Icon(Icons.Outlined.Palette,null);Spacer(Modifier.width(6.dp));Text(stringResource(R.string.color_choose))}
    }
    if(open) AccentColorDialog(value,{open=false}) {onSave(it);open=false}
}

/** 对话框持有草稿；拖动色轮只更新预览，保存才提交设置，取消不改已保存值。 */
@Composable internal fun AccentColorDialog(value: String,onDismiss: ()->Unit,onSave: (String)->Unit) {
    var color by remember {mutableLongStateOf(AccentColor.parse(value) ?: 0xFFA895FF)}
    var hex by remember {mutableStateOf(AccentColor.format(color))}
    var channels by remember {mutableStateOf(listOf(24,16,8,0).map {(color shr it and 255).toString()})}
    var hsv by remember {mutableStateOf(FloatArray(3).also {android.graphics.Color.colorToHSV(color.toInt(),it)})}
    fun accept(c: Long,refreshHsv: Boolean=true) {
        color=c;hex=AccentColor.format(c);channels=listOf(24,16,8,0).map {(c shr it and 255).toString()}
        if(refreshHsv) hsv=FloatArray(3).also {android.graphics.Color.colorToHSV(c.toInt(),it)}
    }
    fun fromHsv(h: Float,s: Float,v: Float) {
        hsv=floatArrayOf(h,s,v)
        accept(android.graphics.Color.HSVToColor((color shr 24 and 255).toInt(),hsv).toLong() and 0xFFFFFFFFL,false)
    }
    val valid=AccentColor.parse(hex)!=null && channels.all {it.toIntOrNull() in 0..255}
    ColorPickerDialog(onDismiss,title={Text(stringResource(R.string.color_choose),style=NonetTheme.typography.titleLarge)},text={
        Column(Modifier.fillMaxWidth(),verticalArrangement=Arrangement.spacedBy(10.dp)) {
            val wheelLabel=stringResource(R.string.color_wheel)
            Box(Modifier.fillMaxWidth(),contentAlignment=Alignment.Center) {
                val update by rememberUpdatedState<(Offset,Float)->Unit> {point,width ->
                    val radius=width/2;val delta=point-Offset(radius,radius)
                    val hue=((atan2(delta.y,delta.x)*180/PI+360)%360).toFloat()
                    fromHsv(hue,(delta.getDistance()/radius).coerceIn(0f,1f),hsv[2])
                }
                Canvas(Modifier.size(210.dp).testTag("accent_color_wheel").semantics {contentDescription=wheelLabel}
                    .pointerInput(Unit) {detectTapGestures {update(it,size.width.toFloat())}}
                    .pointerInput(Unit) {detectDragGestures(onDragStart={update(it,size.width.toFloat())}) {change,_ ->change.consume();update(change.position,size.width.toFloat())}}) {
                    val radius=size.minDimension/2
                    drawCircle(Brush.sweepGradient(listOf(Color.Red,Color.Yellow,Color.Green,Color.Cyan,Color.Blue,Color.Magenta,Color.Red)))
                    drawCircle(Brush.radialGradient(listOf(Color.White,Color.White.copy(alpha=0f)),radius=radius))
                    drawCircle(Color.Black.copy(alpha=1-hsv[2]))
                    val angle=hsv[0]*PI.toFloat()/180
                    val selection=center+Offset(cos(angle),sin(angle))*hsv[1]*radius
                    drawCircle(Color.Black,7.dp.toPx(),selection,style=Stroke(3.dp.toPx()))
                    drawCircle(Color.White,7.dp.toPx(),selection,style=Stroke(1.5.dp.toPx()))
                }
            }
            Text(stringResource(R.string.color_brightness),style=NonetTheme.typography.bodySmall)
            RoundSlider(hsv[2],{fromHsv(hsv[0],hsv[1],it)},Modifier.testTag("accent_brightness"))
            Text(stringResource(R.string.color_alpha),style=NonetTheme.typography.bodySmall)
            RoundSlider((color shr 24 and 255)/255f,{alpha ->accept((color and 0xFFFFFF) or ((alpha*255).roundToInt().toLong() shl 24),false)},Modifier.testTag("accent_alpha"))
            Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(10.dp)) {
                // 棋盘预览能直接观察透明度，不将透明颜色误显示为白色。
                Canvas(Modifier.size(44.dp)) {
                    val cell=size.width/4
                    repeat(4) {y ->repeat(4) {x ->drawRect(if((x+y)%2==0) Color.LightGray else Color.White,Offset(x*cell,y*cell),androidx.compose.ui.geometry.Size(cell,cell))}}
                    drawRect(Color(color.toInt()))
                }
                OutlinedTextField(hex,{raw ->hex=raw.take(9);AccentColor.parse(hex)?.let {accept(it);hex=raw.take(9)}},Modifier.weight(1f).testTag("accent_hex"),label={Text(stringResource(R.string.color_hex))},singleLine=true)
            }
            Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                listOf("A","R","G","B").forEachIndexed {index,label ->
                    OutlinedTextField(channels[index],{raw ->
                        channels=channels.toMutableList().also {it[index]=raw.take(3)}
                        val values=channels.map {it.toIntOrNull()}
                        if(values.all {it!=null}) AccentColor.channels(values[0]!!,values[1]!!,values[2]!!,values[3]!!)?.let {c ->color=c;hex=AccentColor.format(c);hsv=FloatArray(3).also {android.graphics.Color.colorToHSV(c.toInt(),it)}}
                    },Modifier.weight(1f).testTag("accent_$label"),label={Text(label)},singleLine=true,keyboardOptions=KeyboardOptions(keyboardType=KeyboardType.Number))
                }
            }
            if(!valid) Text(stringResource(R.string.color_invalid),color=NonetTheme.colorScheme.primary,style=NonetTheme.typography.bodySmall)
        }
    },confirmButton={val focus=androidx.compose.ui.platform.LocalFocusManager.current;TextButton(onClick={focus.clearFocus();if(valid) onSave(AccentColor.format(color))},enabled=valid,modifier=Modifier.testTag("accent_save")) {Text(stringResource(R.string.save))}},dismissButton={TextButton(onClick=onDismiss) {Text(stringResource(R.string.cancel))}})
}

/** 可用高度扣除输入法与系统栏，短屏和横屏下正文滚动，保存/取消始终可见。 */
@Composable private fun ColorPickerDialog(onDismissRequest: ()->Unit,title: @Composable ()->Unit,text: @Composable ()->Unit,confirmButton: @Composable ()->Unit,dismissButton: @Composable ()->Unit) {
    androidx.compose.ui.window.Dialog(onDismissRequest,properties=androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth=false,decorFitsSystemWindows=false)) {
        BoxWithConstraints(Modifier.fillMaxSize().systemBarsPadding().imePadding().padding(16.dp),contentAlignment=Alignment.Center) {
            Surface(Modifier.widthIn(max=520.dp).fillMaxWidth().heightIn(max=maxHeight).testTag("accent_dialog"),androidx.compose.foundation.shape.RoundedCornerShape(18.dp)) {
                Column(Modifier.padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                    title()
                    Box(Modifier.weight(1f,fill=false).verticalScroll(rememberScrollState())) {text()}
                    Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End) {dismissButton();confirmButton()}
                }
            }
        }
    }
}

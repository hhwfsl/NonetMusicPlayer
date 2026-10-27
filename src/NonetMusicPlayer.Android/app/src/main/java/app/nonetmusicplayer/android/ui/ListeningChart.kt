package app.nonetmusicplayer.android.ui

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.*
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.ListeningBar
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 一个 Canvas 绘制整张图，不为每个柱创建控件；动画仅随实际数据变化启动。 */
@Composable internal fun ListeningChart(title: String,bars: List<ListeningBar>,modifier: Modifier=Modifier,compact: Boolean=false) {
    val maximum=(bars.maxOfOrNull {it.durationMs} ?: 0).coerceAtLeast(60000)
    val color=NonetTheme.colorScheme.primary;val axis=NonetTheme.colorScheme.outlineVariant
    val description=title+": "+bars.joinToString {it.label+" "+timeLabel(it.durationMs)}
    val progress=remember {Animatable(0f)}
    LaunchedEffect(bars) {progress.animateTo(1f,tween(420,easing=FastOutSlowInEasing))}
    Surface(modifier.fillMaxWidth().testTag(if(bars.size==24) "hourly_chart" else "weekly_chart"),RoundedCornerShape(16.dp),NonetTheme.colorScheme.surfaceVariant) {
        Column(Modifier.padding(if(compact) 14.dp else 18.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Text(title,style=NonetTheme.typography.titleSmall,fontWeight=FontWeight.SemiBold)
            Text(stringResource(R.string.chart_total,timeLabel(bars.sumOf {it.durationMs})),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            Text(timeLabel(maximum),style=NonetTheme.typography.labelSmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            Canvas(Modifier.fillMaxWidth().height(if(compact) 68.dp else 118.dp).semantics {contentDescription=description}) {
                val step=size.width/bars.size.coerceAtLeast(1);val width=step*.58f
                drawLine(axis,Offset(0f,size.height),Offset(size.width,size.height),1.dp.toPx())
                bars.forEachIndexed {i,bar ->
                    val height=(bar.durationMs.toFloat()/maximum*size.height*progress.value).coerceAtLeast(1.dp.toPx())
                    drawRoundRect(color,Offset(i*step+(step-width)/2,size.height-height),Size(width,height),CornerRadius(3.dp.toPx(),3.dp.toPx()))
                }
            }
            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween) {
                bars.forEachIndexed {i,bar ->if(bars.size<=7 || i==0 || i==bars.lastIndex || i%6==0) Text(bar.label,style=NonetTheme.typography.labelSmall,color=NonetTheme.colorScheme.onSurfaceVariant)}
            }
        }
    }
}

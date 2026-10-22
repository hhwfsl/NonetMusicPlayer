package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import app.nonetmusicplayer.android.domain.LyricWord

/** 按排版后的字符矩形裁剪主题色，颜色从左到右推进，换行仍属于同一句。 */
@Composable fun KaraokeText(text: String,words: List<LyricWord>,position: Long,active: Boolean,style: TextStyle,positionReader: ()->Long={position}) {
    var layout by remember(text) {mutableStateOf<TextLayoutResult?>(null)}
    val base=NonetTheme.colorScheme.onSurfaceVariant
    Box {
        Text(text,color=if(active && words.isEmpty()) NonetTheme.colorScheme.primary else base,style=style)
        if(active && words.isNotEmpty()) Text(text,color=NonetTheme.colorScheme.primary,style=style,
            onTextLayout={layout=it},modifier=Modifier.drawWithCache {
                val info=layout
                val rectangles=info?.let {List(text.length) {i ->it.getBoundingBox(i)}} ?: emptyList()
                val mask=Path()
                // 字形布局只计算一次；每帧合并裁剪区域后仅绘制一次主题色文字，避免逐字重画整句。
                onDrawWithContent {
                    if(info==null) return@onDrawWithContent
                    val now=positionReader();mask.reset()
                    words.forEach {word ->
                        val progress=((now-word.startMs).toFloat()/(word.endMs-word.startMs).coerceAtLeast(1)).coerceIn(0f,1f)
                        val completed=progress*(word.end-word.start)
                        for(i in word.start.coerceAtLeast(0) until word.end.coerceAtMost(text.length)) {
                            val fraction=(completed-(i-word.start)).coerceIn(0f,1f)
                            if(fraction>0) {
                                val rect=rectangles[i]
                                mask.addRect(androidx.compose.ui.geometry.Rect(rect.left,rect.top,rect.left+rect.width*fraction,rect.bottom))
                            }
                        }
                    }
                    clipPath(mask) {this@onDrawWithContent.drawContent()}
                }
            })
    }
}

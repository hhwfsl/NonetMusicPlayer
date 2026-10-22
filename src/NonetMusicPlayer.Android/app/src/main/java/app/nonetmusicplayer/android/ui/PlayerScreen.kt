package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.animateScrollBy
import androidx.compose.foundation.gestures.scrollBy
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.withInfiniteAnimationFrameNanos
import androidx.compose.foundation.interaction.collectIsDraggedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.QueueMusic
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.input.pointer.pointerInput
import app.nonetmusicplayer.android.ui.musicString as stringResource
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first

@Composable fun MiniPlayer(state: PlaybackState,onOpen: ()->Unit,onPlay: ()->Unit,onQueue: ()->Unit) {
    val track=state.track
    Surface(modifier=Modifier.testTag("mini_player"),color=NonetTheme.colorScheme.surface,onClick={if(track!=null) onOpen()}) {
        Column {
            LinearProgressIndicator(progress={if(state.durationMs>0) (state.positionMs.toFloat()/state.durationMs).coerceIn(0f,1f) else 0f},modifier=Modifier.fillMaxWidth().height(2.dp),drawStopIndicator={})
            Row(Modifier.fillMaxWidth().padding(start=16.dp,end=8.dp,top=8.dp,bottom=8.dp),verticalAlignment=Alignment.CenterVertically) {
                Cover(track?.cover.orEmpty(),Modifier.size(44.dp));Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(track?.title ?: "Nonet",maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.titleSmall)
                    Text(track?.artist?.ifBlank {stringResource(R.string.unknown_artist)} ?: stringResource(R.string.queue_empty),maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                }
                if(state.buffering) CircularProgressIndicator(Modifier.size(22.dp),strokeWidth=2.dp)
                else IconAction(if(state.playing) Icons.Outlined.Pause else Icons.Outlined.PlayArrow,stringResource(if(state.playing) R.string.pause else R.string.play),enabled=track!=null,onClick=onPlay)
                IconAction(Icons.AutoMirrored.Outlined.QueueMusic,stringResource(R.string.queue),onClick=onQueue)
            }
        }
    }
}
@Composable fun PlayerScreen(state: PlaybackState,mode: String,liked: Boolean,lyrics: List<LyricLine>,onBack: ()->Unit,onPlay: ()->Unit,
    onPrevious: ()->Unit,onNext: ()->Unit,onSeek: (Long)->Unit,onMode: ()->Unit,onLike: ()->Unit,onMore: ()->Unit) {
    val track=state.track ?: return
    var showLyrics by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(false) }
    BoxWithConstraints(Modifier.fillMaxSize()) {
    if((maxHeight<480.dp && maxWidth>maxHeight) || maxWidth>=700.dp) {
        CompactPlayer(state,mode,liked,lyrics,showLyrics,{showLyrics=!showLyrics},onBack,onPlay,onPrevious,onNext,onSeek,onMode,onLike,onMore)
        return@BoxWithConstraints
    }
    Column(Modifier.fillMaxSize().padding(horizontal=20.dp),horizontalAlignment=Alignment.CenterHorizontally) {
        Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
            IconAction(Icons.Outlined.KeyboardArrowDown,stringResource(R.string.back),onClick=onBack)
            Text(stringResource(R.string.now_playing),Modifier.weight(1f),textAlign=TextAlign.Center,style=NonetTheme.typography.titleMedium)
            IconAction(Icons.Outlined.MoreHoriz,stringResource(R.string.more),onClick=onMore)
        }
        Row(horizontalArrangement=Arrangement.spacedBy(12.dp)) {
            TextButton(onClick={showLyrics=false}) {Text(stringResource(R.string.artwork),color=if(!showLyrics) NonetTheme.colorScheme.primary else NonetTheme.colorScheme.onSurfaceVariant)}
            TextButton(onClick={showLyrics=true}) {Text(stringResource(R.string.lyrics),color=if(showLyrics) NonetTheme.colorScheme.primary else NonetTheme.colorScheme.onSurfaceVariant)}
        }
        BoxWithConstraints(Modifier.weight(1f).fillMaxWidth(),contentAlignment=Alignment.Center) {
            val landscape=maxWidth>maxHeight*1.5f
            val bodyHeight=maxHeight
            val coverSize=minOf(maxWidth*.85f,maxHeight*.9f,360.dp)
            // 固定两个状态的视口与对齐方式，淡出封面不能被歌词重新测量挤到左上角。
            androidx.compose.animation.Crossfade(showLyrics,modifier=Modifier.fillMaxSize(),animationSpec=tween(240),label="player_content") { lyricsVisible ->
            Box(Modifier.fillMaxSize(),contentAlignment=Alignment.Center) {
            if(lyricsVisible) {
                if(landscape) Row(Modifier.fillMaxSize(),horizontalArrangement=Arrangement.spacedBy(28.dp),verticalAlignment=Alignment.CenterVertically) {
                    Cover(track.cover,Modifier.heightIn(max=bodyHeight*.8f).fillMaxHeight(.8f).aspectRatio(1f))
                    LyricView(lyrics,state.positionMs,onSeek,Modifier.weight(1f).fillMaxHeight(),playing=state.playing) {showLyrics=false}
                } else LyricView(lyrics,state.positionMs,onSeek,Modifier.fillMaxSize(),playing=state.playing) {showLyrics=false}
            } else Cover(track.cover,Modifier.size(coverSize).testTag("player_artwork").clickable {showLyrics=true})
            }
            }
        }
        Column(Modifier.fillMaxWidth().padding(top=16.dp,bottom=12.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
            Text(track.title,style=NonetTheme.typography.headlineSmall,fontWeight=FontWeight.SemiBold,maxLines=2,overflow=TextOverflow.Ellipsis)
            Text(track.artist.ifBlank { stringResource(R.string.unknown_artist) },color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=1,overflow=TextOverflow.Ellipsis)
        }
        var seeking by remember(track.id) { mutableStateOf<Float?>(null) }
        RoundSlider(value=seeking ?: state.positionMs.coerceAtMost(state.durationMs).toFloat(),onValueChange={seeking=it},onValueChangeFinished={seeking?.let {onSeek(it.toLong())};seeking=null},
            valueRange=0f..state.durationMs.coerceAtLeast(1).toFloat(),modifier=Modifier.fillMaxWidth().testTag("playback_slider"))
        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween) {
            Text(timeLabel(seeking?.toLong() ?: state.positionMs),style=NonetTheme.typography.bodyMedium,color=NonetTheme.colorScheme.onSurfaceVariant)
            Text(timeLabel(state.durationMs),style=NonetTheme.typography.bodyMedium,color=NonetTheme.colorScheme.onSurfaceVariant)
        }
        Row(Modifier.fillMaxWidth().padding(vertical=20.dp),horizontalArrangement=Arrangement.SpaceEvenly,verticalAlignment=Alignment.CenterVertically) {
            IconAction(when(mode) {"shuffle" -> Icons.Outlined.Shuffle;"repeat-one" -> Icons.Outlined.RepeatOne;else -> Icons.Outlined.Repeat},stringResource(when(mode){"shuffle" -> R.string.shuffle;"repeat-one" -> R.string.repeat_one;else -> R.string.repeat_all}),onClick=onMode)
            IconAction(Icons.Outlined.SkipPrevious,stringResource(R.string.previous),onClick=onPrevious)
            FilledIconButton(onClick=onPlay,modifier=Modifier.size(72.dp),shape=CircleShape) { if(state.buffering) CircularProgressIndicator(Modifier.size(28.dp),color=NonetTheme.colorScheme.onPrimary,strokeWidth=2.dp) else Icon(if(state.playing) Icons.Outlined.Pause else Icons.Outlined.PlayArrow,stringResource(if(state.playing) R.string.pause else R.string.play),Modifier.size(38.dp)) }
            IconAction(Icons.Outlined.SkipNext,stringResource(R.string.next),onClick=onNext)
            IconAction(if(liked) Icons.Outlined.Favorite else Icons.Outlined.FavoriteBorder,stringResource(if(liked) R.string.remove_liked else R.string.add_liked),tint=if(liked) androidx.compose.ui.graphics.Color(0xFFEF536D) else NonetTheme.colorScheme.onSurfaceVariant,onClick=onLike)
        }
    }
    }
}
/** 手机横屏优先保留播放操作，封面/歌词与控制区域分列，不挤出屏幕。 */
@Composable private fun CompactPlayer(state: PlaybackState,mode: String,liked: Boolean,lyrics: List<LyricLine>,showLyrics: Boolean,onToggleLyrics: ()->Unit,onBack: ()->Unit,onPlay: ()->Unit,
    onPrevious: ()->Unit,onNext: ()->Unit,onSeek: (Long)->Unit,onMode: ()->Unit,onLike: ()->Unit,onMore: ()->Unit) {
    val track=state.track ?: return;var seeking by remember(track.id) {mutableStateOf<Float?>(null)}
    Column(Modifier.fillMaxSize().padding(horizontal=16.dp)) {
        Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
            IconAction(Icons.Outlined.KeyboardArrowDown,stringResource(R.string.back),onClick=onBack)
            Text(stringResource(R.string.now_playing),Modifier.weight(1f),style=NonetTheme.typography.titleMedium)
            IconAction(Icons.Outlined.Subtitles,stringResource(R.string.lyrics),onClick=onToggleLyrics)
            IconAction(Icons.Outlined.MoreHoriz,stringResource(R.string.more),onClick=onMore)
        }
        Row(Modifier.weight(1f).fillMaxWidth(),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(24.dp)) {
            BoxWithConstraints(Modifier.weight(.4f).fillMaxHeight(),contentAlignment=Alignment.Center) {
                val coverSize=minOf(maxWidth,maxHeight*.9f)
                androidx.compose.animation.Crossfade(showLyrics,modifier=Modifier.fillMaxSize(),animationSpec=tween(240),label="compact_player_content") {visible ->
                    Box(Modifier.fillMaxSize(),contentAlignment=Alignment.Center) {
                        if(visible) LyricView(lyrics,state.positionMs,onSeek,Modifier.fillMaxSize(),playing=state.playing,onArtwork=onToggleLyrics)
                        else Cover(track.cover,Modifier.size(coverSize).testTag("player_artwork").clickable(onClick=onToggleLyrics))
                    }
                }
            }
            Column(Modifier.weight(.6f),verticalArrangement=Arrangement.spacedBy(4.dp)) {
                Text(track.title,style=NonetTheme.typography.titleLarge,fontWeight=FontWeight.SemiBold,maxLines=1,overflow=TextOverflow.Ellipsis)
                Text(track.artist.ifBlank {stringResource(R.string.unknown_artist)},style=NonetTheme.typography.bodyMedium,color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=1,overflow=TextOverflow.Ellipsis)
                RoundSlider(seeking ?: state.positionMs.coerceAtMost(state.durationMs).toFloat(),{seeking=it},onValueChangeFinished={seeking?.let {onSeek(it.toLong())};seeking=null},valueRange=0f..state.durationMs.coerceAtLeast(1).toFloat())
                Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween) {Text(timeLabel(seeking?.toLong() ?: state.positionMs));Text(timeLabel(state.durationMs))}
                Row(Modifier.fillMaxWidth().padding(vertical=8.dp),horizontalArrangement=Arrangement.SpaceEvenly,verticalAlignment=Alignment.CenterVertically) {
                    IconAction(when(mode){"shuffle"->Icons.Outlined.Shuffle;"repeat-one"->Icons.Outlined.RepeatOne;else->Icons.Outlined.Repeat},stringResource(when(mode){"shuffle"->R.string.shuffle;"repeat-one"->R.string.repeat_one;else->R.string.repeat_all}),onClick=onMode)
                    IconAction(Icons.Outlined.SkipPrevious,stringResource(R.string.previous),onClick=onPrevious)
                    FilledIconButton(onClick=onPlay,modifier=Modifier.size(60.dp)) {Icon(if(state.playing) Icons.Outlined.Pause else Icons.Outlined.PlayArrow,stringResource(if(state.playing) R.string.pause else R.string.play),Modifier.size(32.dp))}
                    IconAction(Icons.Outlined.SkipNext,stringResource(R.string.next),onClick=onNext)
                    IconAction(if(liked) Icons.Outlined.Favorite else Icons.Outlined.FavoriteBorder,stringResource(if(liked) R.string.remove_liked else R.string.add_liked),tint=if(liked) androidx.compose.ui.graphics.Color(0xFFEF536D) else NonetTheme.colorScheme.onSurfaceVariant,onClick=onLike)
                }
            }
        }
    }
}
@Composable internal fun LyricView(lines: List<LyricLine>,position: Long,onSeek: (Long)->Unit,modifier: Modifier,playing: Boolean=false,onArtwork: ()->Unit={}) {
    val clock=remember(lines) {mutableLongStateOf(position)}
    val wordTimed=remember(lines) {lines.any {it.words.isNotEmpty() || it.translationWords.isNotEmpty()}}
    LaunchedEffect(lines,position,playing) {
        clock.longValue=position
        // 逐行歌词不需要帧循环；逐字歌词采用标准无限动画策略，可由生命周期和测试时钟正常管理。
        if(playing && wordTimed) {
            val anchor=withInfiniteAnimationFrameNanos {it}
            while(true) withInfiniteAnimationFrameNanos {frame ->clock.longValue=position+((frame-anchor)/1_000_000).coerceIn(0,600)}
        }
    }
    // 帧时钟仅在着色绘制阶段消费；换行沿用播放器采样，避免帧状态使整张列表持续重组。
    val current=LrcParser.currentIndex(lines,position)
    val target=current.coerceAtLeast(0)
    val list=rememberLazyListState(initialFirstVisibleItemIndex=target);val dragged by list.interactionSource.collectIsDraggedAsState()
    var preview by remember(lines) {mutableStateOf(false)};var viewport by remember {mutableStateOf(IntSize.Zero)}
    var aligned by remember(lines) {mutableStateOf(false)}
    val centered by remember {derivedStateOf {
        val info=list.layoutInfo;val center=(info.viewportStartOffset+info.viewportEndOffset)/2f
        info.visibleItemsInfo.minByOrNull {kotlin.math.abs(it.offset+it.size/2f-center)}?.index ?: -1
    }}
    val density=androidx.compose.ui.platform.LocalDensity.current
    LaunchedEffect(dragged) { if(dragged) preview=true else if(preview) {delay(5000);preview=false} }
    LaunchedEffect(target,preview,viewport,lines,density.fontScale) {
        if(!preview && lines.isNotEmpty() && viewport.height>0) {
            val immediate=!aligned || list.layoutInfo.visibleItemsInfo.none {it.index==target}
            if(immediate) list.scrollToItem(target)
            val item=snapshotFlow {list.layoutInfo.visibleItemsInfo.firstOrNull {it.index==target && it.size>0}}.filterNotNull().first()
            // offset 的坐标原点不包含顶部留白，必须用 viewport 的实际起止坐标求中心。
            val info=list.layoutInfo;val delta=item.offset+item.size/2f-(info.viewportStartOffset+info.viewportEndOffset)/2f
            if(immediate) list.scrollBy(delta) else if(kotlin.math.abs(delta)>1) list.animateScrollBy(delta,tween(320))
            aligned=true
        }
    }
    // 不用 clickable 合并整张歌词列表的语义，保留每行朗读、当前行状态和子按钮。
    val touch=Modifier.pointerInput(onArtwork) {detectTapGestures(onTap={onArtwork()})}.semantics {onClick {onArtwork();true}}
    if(lines.isEmpty()) Box(modifier.then(touch).testTag("lyrics_view"),contentAlignment=Alignment.Center) {Text(stringResource(R.string.no_lyrics),color=NonetTheme.colorScheme.onSurfaceVariant)}
    else LazyColumn(modifier.then(touch).testTag("lyrics_view").onSizeChanged {viewport=it}.alpha(if(aligned) 1f else 0f),state=list,contentPadding=PaddingValues(vertical=with(density){(viewport.height/2f).toDp()}),verticalArrangement=Arrangement.spacedBy(24.dp)) {
        itemsIndexed(lines,key={i,_ -> i}) { index,line ->
            Row(Modifier.fillMaxWidth().testTag("lyric_line_$index").semantics {selected=index==current},verticalAlignment=Alignment.CenterVertically) {
                Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                    KaraokeText(line.text,line.words,position,index==current,NonetTheme.typography.headlineSmall) {clock.longValue}
                    if(line.translation.isNotBlank()) {
                        val translated=remember(line) {if(line.translationWords.isNotEmpty()) line.translationWords
                            else if(line.words.isNotEmpty()) listOf(LyricWord(line.timeMs,line.endMs,0,line.translation.length)) else emptyList()}
                        KaraokeText(line.translation,translated,position,index==current,NonetTheme.typography.bodyLarge) {clock.longValue}
                    }
                }
                Box(Modifier.width(40.dp)) {if(preview && index==centered && line.timeMs>=0) IconAction(Icons.Outlined.PlayArrow,stringResource(R.string.seek_lyric)) {onSeek(line.timeMs);preview=false}}
            }
        }
    }
}

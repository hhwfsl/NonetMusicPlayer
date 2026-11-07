package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.Track
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 队列排序只改变接下来的播放顺序，当前歌曲及进度保持不变；屏幕阅读器仍可逐项上下移。 */
@Composable internal fun PlaybackQueueList(queue: List<Track>,currentId: String,liked: List<String>,
    onPlay: (Track)->Unit,onLike: (Track)->Unit,onMore: (Track)->Unit,onMove: (Track,Int)->Unit) {
    val up=stringResource(R.string.move_up);val down=stringResource(R.string.move_down)
    SongReorderHost(queue,true,onMove,Modifier.fillMaxWidth().heightIn(max=480.dp),tag="queue_reorder_host",
        draggedContent={TrackRow(it,queue.indexOf(it),it.id==currentId,it.id in liked,{},{},{})}) {list,preview,dragged ->
        LazyColumn(Modifier.fillMaxSize().testTag("playback_queue"),state=list,userScrollEnabled=dragged==null,contentPadding=PaddingValues(8.dp)) {
            if(preview.isEmpty()) item {EmptyState(stringResource(R.string.queue_empty))}
            itemsIndexed(preview,key={_,track->track.id}) {index,track ->
                Box(Modifier.animateItem(fadeInSpec=null,fadeOutSpec=null).alpha(if(dragged==track.id) 0f else 1f).semantics {
                    customActions=listOf(CustomAccessibilityAction(up) {onMove(track,-1);true},CustomAccessibilityAction(down) {onMove(track,1);true})
                }) {TrackRow(track,index,track.id==currentId,track.id in liked,{onPlay(track)},{onLike(track)},{onMore(track)})}
            }
        }
    }
}

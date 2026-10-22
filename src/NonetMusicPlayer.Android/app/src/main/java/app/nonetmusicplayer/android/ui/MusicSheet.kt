package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

/** 操作菜单在小屏及大字体模式下允许滚动，保证最后一个动作仍可触达。 */
@Composable fun MusicSheet(onDismissRequest: ()->Unit,content: @Composable ColumnScope.()->Unit) {
    ModalBottomSheet(onDismissRequest=onDismissRequest) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()),content=content)
    }
}

package app.nonetmusicplayer.android.network

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import app.nonetmusicplayer.android.MusicApplication

/** 通知按钮只响应当前随机请求 ID；不启动 Activity，不依赖界面焦点，也不自动授权过期请求。 */
class PairingActionReceiver: BroadcastReceiver() {
    override fun onReceive(context: Context,intent: Intent) {
        val id=intent.getStringExtra("requestId") ?: return
        (context.applicationContext as MusicApplication).lan.answerConnection(id,intent.action=="nonet.lan.accept")
    }
}

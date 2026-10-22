package app.nonetmusicplayer.android.domain

import java.io.ByteArrayOutputStream
import java.io.InputStream

/** 有界读取小型文档，兼容 API 28；音频本体不得调用本方法。 */
fun InputStream.readLimited(limit: Int): ByteArray {
    val output=ByteArrayOutputStream();val buffer=ByteArray(8192)
    while(true) {
        val count=read(buffer,0,minOf(buffer.size,limit+1-output.size()))
        if(count<0) break
        output.write(buffer,0,count);require(output.size()<=limit) { "Document too large" }
    }
    return output.toByteArray()
}

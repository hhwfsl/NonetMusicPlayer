package app.nonetmusicplayer.android.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.*
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.readLimited
import app.nonetmusicplayer.android.ui.musicString as stringResource
import kotlinx.coroutines.*
import java.io.File
import java.util.UUID
import kotlin.math.*

/** 先限制输入大小和像素，再提供拖动/双指缩放的裁剪；原图不会被修改。 */
@Composable fun ImageCropper(uri: Uri, ratio: Float, onClose: ()->Unit, onSaved: (String)->Unit, onError: ()->Unit) {
    val context=LocalContext.current;val scope=rememberCoroutineScope()
    var bitmap by remember(uri) {mutableStateOf<Bitmap?>(null)}
    var zoom by remember {mutableFloatStateOf(1f)}
    var offset by remember {mutableStateOf(Offset.Zero)}
    var size by remember {mutableStateOf(IntSize.Zero)}
    var saving by remember {mutableStateOf(false)}
    LaunchedEffect(uri) {
        try { bitmap=withContext(Dispatchers.IO) {
            val bytes=context.contentResolver.openInputStream(uri)?.use {it.readLimited(20_000_000)} ?: error("Image unavailable")
            val options=BitmapFactory.Options().apply {inJustDecodeBounds=true}
            BitmapFactory.decodeByteArray(bytes,0,bytes.size,options)
            require(options.outWidth>0 && options.outHeight>0 && options.outWidth.toLong()*options.outHeight<=100_000_000)
            var sample=1;while(max(options.outWidth,options.outHeight)/sample>2048) sample*=2
            BitmapFactory.decodeByteArray(bytes,0,bytes.size,BitmapFactory.Options().apply {inSampleSize=sample}) ?: error("Invalid image")
        } } catch(cancel: CancellationException) {throw cancel} catch(error: Throwable) {onError();onClose()}
    }
    DisposableEffect(bitmap) {
        // 捕获本次 effect 所拥有的位图，不能在旧 effect 释放时读取后来加载的新位图。
        val ownedBitmap=bitmap
        onDispose {ownedBitmap?.recycle()}
    }
    Dialog(onDismissRequest={if(!saving) onClose()},properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.fillMaxWidth(.94f).widthIn(max=800.dp),shape=NonetTheme.shapes.extraLarge) {
            BoxWithConstraints {
                val viewportHeight=maxHeight
                Column(Modifier.padding(20.dp),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                    Text(stringResource(R.string.crop_image),style=NonetTheme.typography.titleLarge)
                    if(viewportHeight>=400.dp) Text(stringResource(R.string.crop_hint),style=NonetTheme.typography.bodySmall)
                    val available=(viewportHeight-if(viewportHeight<400.dp) 220.dp else 260.dp).coerceAtLeast(40.dp).coerceAtMost(460.dp)
                    Canvas(Modifier.fillMaxWidth().heightIn(max=available).aspectRatio(ratio,matchHeightConstraintsFirst=true)
                        .background(NonetTheme.colorScheme.surfaceVariant).onSizeChanged {size=it}
                        .pointerInput(bitmap,size) {
                            detectTransformGestures { _,pan,scale,_ ->
                                zoom=(zoom*scale).coerceIn(1f,4f)
                                bitmap?.let {b ->
                                    val base=max(size.width.toFloat()/b.width,size.height.toFloat()/b.height)
                                    val bounds=Offset((b.width*base*zoom-size.width)/2,(b.height*base*zoom-size.height)/2)
                                    offset=Offset((offset.x+pan.x).coerceIn(-bounds.x,bounds.x),(offset.y+pan.y).coerceIn(-bounds.y,bounds.y))
                                }
                            }
                        }) {
                        bitmap?.let {b ->
                            val base=max(this.size.width/b.width,this.size.height/b.height)*zoom
                            val w=(b.width*base).roundToInt();val h=(b.height*base).roundToInt()
                            drawImage(b.asImageBitmap(),dstOffset=IntOffset(((this.size.width-w)/2+offset.x).roundToInt(),((this.size.height-h)/2+offset.y).roundToInt()),dstSize=IntSize(w,h))
                        }
                    }
                    RoundSlider(zoom,{zoom=it;offset=Offset.Zero},valueRange=1f..4f)
                    Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End) {
                        TextButton(enabled=!saving,onClick=onClose) {Text(stringResource(R.string.cancel))}
                        Button(enabled=bitmap!=null && !saving,onClick={
                            saving=true
                            scope.launch {
                                try {val path=withContext(Dispatchers.IO) {
                                    val b=bitmap ?: error("Image unavailable")
                                    val width=1024;val height=(width/ratio).roundToInt().coerceIn(256,2048)
                                    val result=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888)
                                    val canvas=android.graphics.Canvas(result)
                                    val scale=max(width.toFloat()/b.width,height.toFloat()/b.height)*zoom
                                    canvas.translate((width-b.width*scale)/2+offset.x/size.width*width,(height-b.height*scale)/2+offset.y/size.height*height)
                                    canvas.scale(scale,scale);canvas.drawBitmap(b,0f,0f,android.graphics.Paint(android.graphics.Paint.FILTER_BITMAP_FLAG))
                                    val file=File(context.filesDir,"Images/${UUID.randomUUID()}.jpg").apply {parentFile?.mkdirs()}
                                    try {file.outputStream().use {result.compress(Bitmap.CompressFormat.JPEG,94,it)}} finally {result.recycle()}
                                    file.absolutePath
                                };onSaved(path);onClose()}
                                catch(cancel: CancellationException) {throw cancel}
                                catch(error: Throwable) {saving=false;onError()}
                            }
                        }) {Text(stringResource(R.string.save))}
                    }
                }
            }
        }
    }
}

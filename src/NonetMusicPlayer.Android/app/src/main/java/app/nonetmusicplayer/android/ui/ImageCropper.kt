package app.nonetmusicplayer.android.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.graphics.RectF
import android.net.Uri
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.*
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.readLimited
import app.nonetmusicplayer.android.domain.CropGeometry
import app.nonetmusicplayer.android.domain.CropTransform
import app.nonetmusicplayer.android.ui.musicString as stringResource
import kotlinx.coroutines.*
import java.io.File
import java.util.UUID
import kotlin.math.*

/** 固定裁剪框只显示实际保留部分；图片始终铺满框，手势与导出使用同一份几何快照。 */
@Composable fun ImageCropper(uri: Uri, ratio: Float, onClose: ()->Unit, onSaved: (String)->Unit, onError: ()->Unit) {
    val context=LocalContext.current;val scope=rememberCoroutineScope()
    val cropRatio=if(ratio.isFinite() && ratio>0) ratio.coerceIn(.05f,20f) else 1f
    var bitmap by remember(uri) {mutableStateOf<Bitmap?>(null)}
    var transform by remember(uri,cropRatio) {mutableStateOf(CropTransform())}
    var frameSize by remember(uri,cropRatio) {mutableStateOf(IntSize.Zero)}
    var geometry by remember(uri,cropRatio) {mutableStateOf<CropGeometry?>(null)}
    var saving by remember(uri) {mutableStateOf(false)}
    var saveJob by remember(uri) {mutableStateOf<Job?>(null)}
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
        val ownedBitmap=bitmap
        onDispose {
            // 导出尚在后台读取时延后释放，避免关闭页面与位图绘制竞争。
            val pending=saveJob
            if(pending!=null && !pending.isCompleted) pending.invokeOnCompletion {ownedBitmap?.recycle()}
            else ownedBitmap?.recycle()
        }
    }
    LaunchedEffect(bitmap,frameSize,cropRatio) {
        val b=bitmap
        if(b!=null && frameSize.width>0 && frameSize.height>0) {
            val next=CropGeometry(b.width,b.height,frameSize.width.toFloat(),frameSize.height.toFloat())
            transform=geometry?.let {next.reframe(transform,it)} ?: next.constrain(transform)
            geometry=next
        }
    }
    Dialog(onDismissRequest={if(!saving) onClose()},properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.widthIn(max=800.dp).fillMaxWidth(.94f).heightIn(max=700.dp).testTag("crop_dialog"),shape=NonetTheme.shapes.extraLarge) {
            BoxWithConstraints {
                val compact=maxHeight<420.dp
                Column(Modifier.fillMaxSize().padding(if(compact) 12.dp else 20.dp),verticalArrangement=Arrangement.spacedBy(if(compact) 8.dp else 12.dp)) {
                    Text(stringResource(R.string.crop_image),style=NonetTheme.typography.titleLarge)
                    if(!compact) Text(stringResource(R.string.crop_hint),style=NonetTheme.typography.bodySmall)
                    BoxWithConstraints(Modifier.fillMaxWidth().weight(1f).clip(RoundedCornerShape(12.dp)).background(Color.Black).testTag("crop_stage")) {
                        val availableWidth=(maxWidth-24.dp).coerceAtLeast(1.dp)
                        val availableHeight=(maxHeight-24.dp).coerceAtLeast(1.dp)
                        val frameWidth=minOf(availableWidth,availableHeight*cropRatio)
                        val frameHeight=frameWidth/cropRatio
                        Box(Modifier.align(Alignment.Center).size(frameWidth,frameHeight).testTag("crop_frame")) {
                            Canvas(Modifier.fillMaxSize().clipToBounds().onSizeChanged {frameSize=it}.testTag("crop_image")
                                .pointerInput(bitmap,geometry,saving) {
                                    if(!saving) detectTransformGestures {centroid,pan,scale,_ ->
                                        geometry?.let {transform=it.transform(transform,centroid.x,centroid.y,pan.x,pan.y,scale)}
                                    }
                                }) {
                                val b=bitmap;val g=geometry
                                if(b!=null && g!=null) {
                                    // 布局先于状态协程完成的旋转首帧也按新框铺满，避免短暂露底。
                                    val drawGeometry=if(g.frameWidth==this.size.width && g.frameHeight==this.size.height) g else CropGeometry(b.width,b.height,this.size.width,this.size.height)
                                    val actual=if(drawGeometry===g) g.constrain(transform) else drawGeometry.reframe(transform,g)
                                    val scale=drawGeometry.scale(actual)
                                    withTransform({translate((this@Canvas.size.width-b.width*scale)/2+actual.panX,(this@Canvas.size.height-b.height*scale)/2+actual.panY);scale(scale,scale,pivot=Offset.Zero)}) {
                                        drawImage(b.asImageBitmap())
                                    }
                                }
                            }
                            // 框与图片分层绘制，辅助线不写入导出；所有图像像素仅在框内可见。
                            Canvas(Modifier.matchParentSize()) {
                                val stroke=1.5.dp.toPx();val corner=minOf(20.dp.toPx(),size.minDimension/4)
                                for(part in 1..2) {
                                    drawLine(Color.White.copy(alpha=.3f),Offset(size.width*part/3,0f),Offset(size.width*part/3,size.height),1.dp.toPx())
                                    drawLine(Color.White.copy(alpha=.3f),Offset(0f,size.height*part/3),Offset(size.width,size.height*part/3),1.dp.toPx())
                                }
                                drawRect(Color.White,topLeft=Offset(stroke/2,stroke/2),size=androidx.compose.ui.geometry.Size((size.width-stroke).coerceAtLeast(0f),(size.height-stroke).coerceAtLeast(0f)),style=Stroke(stroke))
                                for(x in listOf(0f,size.width)) for(y in listOf(0f,size.height)) {
                                    val dx=if(x==0f) 1 else -1;val dy=if(y==0f) 1 else -1
                                    drawLine(Color.White,Offset(x,y),Offset(x+dx*corner,y),4.dp.toPx())
                                    drawLine(Color.White,Offset(x,y),Offset(x,y+dy*corner),4.dp.toPx())
                                }
                            }
                        }
                    }
                    RoundSlider(transform.zoom,{value ->if(!saving) geometry?.let {transform=it.zoom(transform,value)}},Modifier.testTag("crop_zoom"),valueRange=1f..4f)
                    Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End) {
                        TextButton(modifier=Modifier.testTag("crop_cancel"),enabled=!saving,onClick=onClose) {Text(stringResource(R.string.cancel))}
                        Button(modifier=Modifier.testTag("crop_save"),enabled=bitmap!=null && geometry!=null && !saving,onClick={
                            val source=bitmap;val snapshot=geometry;val placement=transform
                            if(source!=null && snapshot!=null) {
                            saving=true;saveJob=scope.launch {
                                var generated: File?=null;var delivered=false
                                try {val path=withContext(Dispatchers.IO) {
                                    val result=cropBitmap(source,snapshot,placement,if(cropRatio==1f) 1024 else 2048)
                                    val file=File(context.filesDir,"Images/${UUID.randomUUID()}.jpg").apply {parentFile?.mkdirs()}
                                    generated=file
                                    try {file.outputStream().use {check(result.compress(Bitmap.CompressFormat.JPEG,94,it))}} finally {result.recycle()}
                                    currentCoroutineContext().ensureActive()
                                    file.absolutePath
                                };onSaved(path);delivered=true;onClose()}
                                catch(cancel: CancellationException) {throw cancel}
                                catch(error: Throwable) {saving=false;onError()}
                                finally {if(!delivered) withContext(NonCancellable+Dispatchers.IO) {generated?.delete()}}
                            }
                            }
                        }) {Text(stringResource(R.string.save))}
                    }
                }
            }
        }
    }
}

/** 从预览的同一个源矩形导出，不因输出尺寸取整改变用户选中的位置。 */
internal fun cropBitmap(source: Bitmap, geometry: CropGeometry, transform: CropTransform, maxSide: Int): Bitmap {
    val region=geometry.region(transform);val (width,height)=geometry.outputSize(maxSide)
    val result=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888)
    try {
        val matrix=Matrix().apply {setRectToRect(RectF(region.left,region.top,region.right,region.bottom),RectF(0f,0f,width.toFloat(),height.toFloat()),Matrix.ScaleToFit.FILL)}
        android.graphics.Canvas(result).apply {drawColor(android.graphics.Color.BLACK);drawBitmap(source,matrix,android.graphics.Paint(android.graphics.Paint.FILTER_BITMAP_FLAG))}
        return result
    } catch(error: Throwable) {result.recycle();throw error}
}

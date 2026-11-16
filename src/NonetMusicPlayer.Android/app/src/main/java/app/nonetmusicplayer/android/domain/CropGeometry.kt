package app.nonetmusicplayer.android.domain

import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/** 固定裁剪框的纯几何规则：手势、滑块、窗口变化与导出共用，不依赖界面或 Android 位图。 */
data class CropTransform(val zoom: Float = 1f, val panX: Float = 0f, val panY: Float = 0f)
data class CropRegion(val left: Float, val top: Float, val right: Float, val bottom: Float)
class CropGeometry(val imageWidth: Int, val imageHeight: Int, val frameWidth: Float, val frameHeight: Float) {
    init {require(imageWidth > 0 && imageHeight > 0 && frameWidth.isFinite() && frameHeight.isFinite() && frameWidth > 0 && frameHeight > 0)}
    private val base=max(frameWidth/imageWidth,frameHeight/imageHeight)
    fun scale(value: CropTransform)=base*constrain(value).zoom
    fun constrain(value: CropTransform): CropTransform {
        val zoom=if(value.zoom.isFinite()) value.zoom.coerceIn(1f,4f) else 1f
        val x=max(0f,(imageWidth*base*zoom-frameWidth)/2f)
        val y=max(0f,(imageHeight*base*zoom-frameHeight)/2f)
        return CropTransform(zoom,if(value.panX.isFinite()) value.panX.coerceIn(-x,x) else 0f,if(value.panY.isFinite()) value.panY.coerceIn(-y,y) else 0f)
    }
    fun transform(value: CropTransform, centroidX: Float, centroidY: Float, panX: Float, panY: Float, factor: Float): CropTransform {
        val old=constrain(value)
        val nextZoom=(old.zoom*(if(factor.isFinite() && factor>0) factor else 1f)).coerceIn(1f,4f)
        val applied=nextZoom/old.zoom
        // 保持双指中心下的图片位置；先按实际生效的倍率变换，再限制到始终铺满裁剪框的范围。
        val focusX=centroidX-frameWidth/2f;val focusY=centroidY-frameHeight/2f
        return constrain(CropTransform(nextZoom,focusX+(old.panX-focusX)*applied+panX,focusY+(old.panY-focusY)*applied+panY))
    }
    fun zoom(value: CropTransform, zoom: Float)=transform(value,frameWidth/2f,frameHeight/2f,0f,0f,zoom/constrain(value).zoom)
    fun reframe(value: CropTransform, previous: CropGeometry): CropTransform {
        val old=previous.constrain(value);val factor=base/previous.base
        return constrain(old.copy(panX=old.panX*factor,panY=old.panY*factor))
    }
    fun region(value: CropTransform): CropRegion {
        val actual=constrain(value);val scale=base*actual.zoom
        val width=min(imageWidth.toFloat(),frameWidth/scale);val height=min(imageHeight.toFloat(),frameHeight/scale)
        val left=(imageWidth/2f-(frameWidth/2f+actual.panX)/scale).coerceIn(0f,max(0f,imageWidth-width))
        val top=(imageHeight/2f-(frameHeight/2f+actual.panY)/scale).coerceIn(0f,max(0f,imageHeight-height))
        return CropRegion(left,top,left+width,top+height)
    }
    fun outputSize(maxSide: Int): Pair<Int,Int> {
        require(maxSide>0)
        return if(frameWidth>=frameHeight) maxSide to (maxSide*frameHeight/frameWidth).roundToInt().coerceAtLeast(1)
        else (maxSide*frameWidth/frameHeight).roundToInt().coerceAtLeast(1) to maxSide
    }
}

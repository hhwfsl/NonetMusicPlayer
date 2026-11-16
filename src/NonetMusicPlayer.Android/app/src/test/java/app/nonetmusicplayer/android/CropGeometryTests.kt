package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.CropGeometry
import app.nonetmusicplayer.android.domain.CropTransform
import org.junit.Assert.*
import org.junit.Test
import kotlin.math.abs
import kotlin.random.Random

/** 边界测试用源图坐标验证不露底、焦点连续和输出比例，不依赖截屏或具体手势实现。 */
class CropGeometryTests {
    @Test fun extremeDragsAndPinchesNeverExposeOutsideTheImage() {
        val random=Random(27)
        for((width,height) in listOf(4000 to 1000,1000 to 4000,512 to 512,12 to 4000,8192 to 12)) {
            for((frameWidth,frameHeight) in listOf(300f to 300f,360f to 780f,780f to 360f,180f to 40f)) {
                val geometry=CropGeometry(width,height,frameWidth,frameHeight);var value=CropTransform()
                repeat(1500) {
                    value=geometry.transform(value,random.nextFloat()*frameWidth,random.nextFloat()*frameHeight,(random.nextFloat()-.5f)*10000,(random.nextFloat()-.5f)*10000,if(it%2==0) .02f else 40f)
                    val region=geometry.region(value)
                    assertTrue(value.zoom in 1f..4f)
                    assertTrue(region.left>=-.002f && region.top>=-.002f && region.right<=width+.002f && region.bottom<=height+.002f)
                    assertTrue(region.right>region.left && region.bottom>region.top)
                    assertEquals(frameWidth/frameHeight,(region.right-region.left)/(region.bottom-region.top),.003f)
                }
            }
        }
    }
    @Test fun pinchKeepsTheImageUnderTheFingersAndClampedZoomDoesNotDrift() {
        val geometry=CropGeometry(1000,1000,300f,300f)
        val before=geometry.region(CropTransform())
        val after=geometry.transform(CropTransform(),80f,120f,0f,0f,2f)
        val region=geometry.region(after)
        assertEquals(before.left+80/geometry.scale(CropTransform()),region.left+80/geometry.scale(after),.001f)
        assertEquals(before.top+120/geometry.scale(CropTransform()),region.top+120/geometry.scale(after),.001f)
        val maximum=geometry.constrain(CropTransform(4f,35f,-20f))
        assertEquals(maximum,geometry.transform(maximum,20f,260f,0f,0f,20f))
    }
    @Test fun sliderAndResizeKeepTheSelectedSourceCenter() {
        val geometry=CropGeometry(1600,900,300f,300f);val before=CropTransform(2f,50f,30f)
        val after=geometry.zoom(before,3f)
        val a=geometry.region(before);val b=geometry.region(after)
        assertEquals((a.left+a.right)/2,(b.left+b.right)/2,.001f)
        assertEquals((a.top+a.bottom)/2,(b.top+b.bottom)/2,.001f)
        val resized=CropGeometry(1600,900,600f,600f)
        val c=resized.region(resized.reframe(before,geometry))
        assertEquals(a.left,c.left,.001f);assertEquals(a.top,c.top,.001f)
        assertEquals(a.right,c.right,.001f);assertEquals(a.bottom,c.bottom,.001f)
    }
    @Test fun outputKeepsPortraitAndWideRatiosWithoutTheOldHeightClamp() {
        for(ratio in listOf(.2f,.45f,1f,2.4f,5f)) {
            val g=CropGeometry(2000,1800,400f*ratio,400f);val (width,height)=g.outputSize(2048)
            assertEquals(2048,maxOf(width,height));assertTrue(minOf(width,height)>0)
            assertTrue(abs(width-height*ratio)<=maxOf(1f,ratio))
        }
        val g=CropGeometry(1000,1000,300f,300f)
        assertEquals(CropTransform(),g.constrain(CropTransform(Float.NaN,Float.POSITIVE_INFINITY,Float.NaN)))
        assertEquals(1024 to 1024,g.outputSize(1024))
    }
}

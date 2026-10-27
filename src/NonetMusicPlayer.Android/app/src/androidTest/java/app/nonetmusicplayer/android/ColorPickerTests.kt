package app.nonetmusicplayer.android

import androidx.activity.compose.setContent
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.domain.Preferences
import app.nonetmusicplayer.android.ui.*
import org.junit.*
import org.junit.Assert.*
import java.io.File

/** 真正操作输入和拖动，验证透明颜色能保存，非法输入和取消不能写入设置。 */
@UnstableApi
class ColorPickerTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private fun input(tag: String)=compose.onNode(hasSetTextAction() and hasAnyAncestor(hasTestTag(tag)),useUnmergedTree=true)
    @Test fun timerUsesTypedMinutesWithoutAnExtraEnter() {
        val app=compose.activity.application as MusicApplication
        val preferences=Preferences(language="en")
        var saved=preferences
        compose.activity.setContent {MusicTheme(preferences) {androidx.compose.foundation.layout.Column {SleepTimerSettings(preferences,{saved=it},{throw AssertionError("Invalid duration")},app.sleepTimer)}}}
        input("sleep_minutes").performTextClearance();input("sleep_minutes").performTextInput("7")
        compose.onNodeWithTag("sleep_start").performClick()
        assertEquals(7,saved.sleepMinutes)
        assertTrue(app.sleepTimer.state.value.remainingSeconds in 415..420)
        compose.onNodeWithTag("sleep_cancel").performClick();assertFalse(app.sleepTimer.state.value.active)
    }
    @Test fun hexArgbWheelAndCancelWork() {
        var saved="";var dismissed=false
        compose.activity.setContent {MusicTheme(Preferences(language="en")) {AccentColorDialog("#112233",{dismissed=true}) {saved=it}}}
        input("accent_hex").performTextClearance();input("accent_hex").performTextInput("#80123456")
        compose.onNodeWithTag("accent_save").performClick();assertEquals("#80123456",saved)
        input("accent_R").performScrollTo().performTextClearance();input("accent_R").performTextInput("999")
        compose.onNodeWithTag("accent_save").assertIsNotEnabled()
        input("accent_R").performTextClearance();input("accent_R").performTextInput("240")
        compose.onNodeWithTag("accent_save").performClick();assertEquals("#80F03456",saved)
        compose.onNodeWithTag("accent_color_wheel").performScrollTo().performTouchInput {swipe(center,centerRight-androidx.compose.ui.geometry.Offset(12f,0f),300)}
        compose.onNodeWithTag("accent_save").performClick();assertNotEquals("#80F03456",saved)
        val folder=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        File(folder,"beta10-color-picker.png").outputStream().use {compose.onNodeWithTag("accent_dialog").captureToImage().asAndroidBitmap().compress(android.graphics.Bitmap.CompressFormat.PNG,100,it)}
        compose.onNodeWithText("Cancel").performClick();assertTrue(dismissed)
    }
}

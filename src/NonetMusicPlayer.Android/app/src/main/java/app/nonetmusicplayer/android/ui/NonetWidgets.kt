package app.nonetmusicplayer.android.ui

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.triStateToggleable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.*
import androidx.compose.foundation.text.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.*
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.semantics.*
import androidx.compose.ui.state.ToggleableState
import androidx.compose.ui.text.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.*
import androidx.compose.ui.unit.*
import androidx.compose.ui.window.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** 仅依赖 Foundation 的扁平控件层，不使用 Material 控件、涟漪、浮动标签或胶囊样式。 */
internal val LocalContentColor=staticCompositionLocalOf {Color(0xFF202329)}

@Composable fun Text(text: String,modifier: Modifier=Modifier,color: Color=Color.Unspecified,
    style: TextStyle=NonetTheme.typography.bodyMedium,fontWeight: FontWeight?=null,textAlign: TextAlign?=null,
    maxLines: Int=Int.MAX_VALUE,overflow: TextOverflow=TextOverflow.Clip,onTextLayout: ((TextLayoutResult)->Unit)?=null) {
    BasicText(text,modifier,style.merge(TextStyle(color=if(color==Color.Unspecified) LocalContentColor.current else color,fontWeight=fontWeight,textAlign=textAlign ?: TextAlign.Unspecified)),onTextLayout=onTextLayout ?: {},maxLines=maxLines,overflow=overflow)
}
@Composable fun Icon(imageVector: ImageVector,contentDescription: String?,modifier: Modifier=Modifier,tint: Color=LocalContentColor.current) {
    Image(imageVector,contentDescription,modifier.size(24.dp),colorFilter=ColorFilter.tint(tint),contentScale=ContentScale.Fit)
}
@Composable fun Surface(modifier: Modifier=Modifier,shape: Shape=RectangleShape,color: Color=NonetTheme.colorScheme.surface,border: BorderStroke?=null,onClick: (() -> Unit)?=null,content: @Composable ()->Unit) {
    var surface=modifier.clip(shape).background(color)
    if(border!=null) surface=surface.border(border,shape)
    if(onClick!=null) surface=surface.clickable(onClick=onClick)
    Box(surface) {content()}
}
@Composable private fun ActionButton(onClick: ()->Unit,modifier: Modifier,enabled: Boolean,shape: Shape,kind: Int,content: @Composable RowScope.()->Unit) {
    val c=NonetTheme.colorScheme
    val fill=when(kind) {0->c.primary;1->c.surface;else->Color.Transparent}
    val foreground=(if(kind==0) c.onPrimary else c.primary).copy(alpha=if(enabled) 1f else .4f)
    val stroke=if(kind==1) BorderStroke(1.dp,c.outlineVariant) else null
    Surface(modifier.heightIn(min=44.dp).then(if(enabled) Modifier.clickable(role=Role.Button,onClick=onClick) else Modifier.semantics {disabled()}),shape,fill,stroke) {
        CompositionLocalProvider(LocalContentColor provides foreground) {
            Row(Modifier.padding(horizontal=if(kind==2) 10.dp else 16.dp,vertical=10.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.Center,content=content)
        }
    }
}
@Composable fun Button(onClick: ()->Unit,modifier: Modifier=Modifier,enabled: Boolean=true,shape: Shape=RoundedCornerShape(10.dp),content: @Composable RowScope.()->Unit)=ActionButton(onClick,modifier,enabled,shape,0,content)
@Composable fun OutlinedButton(onClick: ()->Unit,modifier: Modifier=Modifier,enabled: Boolean=true,shape: Shape=RoundedCornerShape(10.dp),content: @Composable RowScope.()->Unit)=ActionButton(onClick,modifier,enabled,shape,1,content)
@Composable fun TextButton(onClick: ()->Unit,modifier: Modifier=Modifier,enabled: Boolean=true,content: @Composable RowScope.()->Unit)=ActionButton(onClick,modifier,enabled,RoundedCornerShape(8.dp),2,content)
@Composable fun IconButton(onClick: ()->Unit,modifier: Modifier=Modifier,enabled: Boolean=true,content: @Composable ()->Unit) {
    Box(modifier.size(48.dp).clip(RoundedCornerShape(8.dp)).then(if(enabled) Modifier.clickable(role=Role.Button,onClick=onClick) else Modifier.semantics {disabled()}),contentAlignment=Alignment.Center) {content()}
}
@Composable fun FilledIconButton(onClick: ()->Unit,modifier: Modifier=Modifier,enabled: Boolean=true,shape: Shape=RoundedCornerShape(10.dp),content: @Composable ()->Unit) {
    Surface(modifier.size(48.dp),shape,NonetTheme.colorScheme.primary,onClick=if(enabled) onClick else null) {
        CompositionLocalProvider(LocalContentColor provides NonetTheme.colorScheme.onPrimary) {Box(Modifier.fillMaxSize(),contentAlignment=Alignment.Center) {content()}}
    }
}
@Composable fun FilledTonalIconButton(onClick: ()->Unit,modifier: Modifier=Modifier,content: @Composable ()->Unit)=IconButton(onClick,modifier,content=content)

/** 标签放在输入框上方；输入、键盘及失焦提交仍由调用方持有，避免隐藏写入行为。 */
@Composable fun OutlinedTextField(value: String,onValueChange: (String)->Unit,modifier: Modifier=Modifier,label: (@Composable ()->Unit)?=null,
    singleLine: Boolean=false,maxLines: Int=Int.MAX_VALUE,shape: Shape=RoundedCornerShape(8.dp),
    keyboardOptions: KeyboardOptions=KeyboardOptions.Default,keyboardActions: KeyboardActions=KeyboardActions.Default,
    visualTransformation: VisualTransformation=VisualTransformation.None) {
    Column(modifier,verticalArrangement=Arrangement.spacedBy(7.dp)) {
        if(label!=null) CompositionLocalProvider(LocalContentColor provides NonetTheme.colorScheme.onSurfaceVariant) {label()}
        BasicTextField(value,onValueChange,Modifier.fillMaxWidth().clip(shape).background(NonetTheme.colorScheme.surface).border(1.dp,NonetTheme.colorScheme.outlineVariant,shape).padding(horizontal=12.dp,vertical=13.dp),
            textStyle=NonetTheme.typography.bodyMedium.copy(color=NonetTheme.colorScheme.onSurface),singleLine=singleLine,maxLines=if(singleLine) 1 else maxLines,
            keyboardOptions=keyboardOptions,keyboardActions=keyboardActions,visualTransformation=visualTransformation,cursorBrush=SolidColor(NonetTheme.colorScheme.primary))
    }
}
@Composable fun HorizontalDivider(modifier: Modifier=Modifier,color: Color=NonetTheme.colorScheme.outlineVariant) {Box(modifier.fillMaxWidth().height(1.dp).background(color))}
@Composable fun Switch(checked: Boolean,onCheckedChange: (Boolean)->Unit,modifier: Modifier=Modifier) {
    val c=NonetTheme.colorScheme
    Box(modifier.size(60.dp,48.dp).toggleable(checked,role=Role.Switch,onValueChange=onCheckedChange),contentAlignment=Alignment.Center) {
        Box(Modifier.size(42.dp,24.dp).background(if(checked) c.primary else c.outlineVariant,CircleShape)) {
            Box(Modifier.align(if(checked) Alignment.CenterEnd else Alignment.CenterStart).padding(3.dp).size(18.dp).background(Color.White,CircleShape))
        }
    }
}
@Composable fun TriStateCheckbox(state: ToggleableState,onClick: ()->Unit,modifier: Modifier=Modifier) {
    val c=NonetTheme.colorScheme
    Box(modifier.size(44.dp).triStateToggleable(state,role=Role.Checkbox,onClick=onClick),contentAlignment=Alignment.Center) {
        Canvas(Modifier.size(18.dp)) {
            val active=state!=ToggleableState.Off
            drawRoundRect(if(active) c.primary else c.outlineVariant,cornerRadius=CornerRadius(3.dp.toPx()),style=if(active) androidx.compose.ui.graphics.drawscope.Fill else Stroke(1.5.dp.toPx()))
            if(state==ToggleableState.On) drawPath(Path().apply {moveTo(size.width*.22f,size.height*.51f);lineTo(size.width*.43f,size.height*.72f);lineTo(size.width*.8f,size.height*.28f)},c.onPrimary,style=Stroke(2.dp.toPx(),cap=StrokeCap.Round))
            else if(state==ToggleableState.Indeterminate) drawLine(c.onPrimary,Offset(size.width*.25f,size.height*.5f),Offset(size.width*.75f,size.height*.5f),2.dp.toPx())
        }
    }
}
@Composable fun Checkbox(checked: Boolean,onCheckedChange: (Boolean)->Unit,modifier: Modifier=Modifier)=TriStateCheckbox(if(checked) ToggleableState.On else ToggleableState.Off,{onCheckedChange(!checked)},modifier)

@Composable fun CircularProgressIndicator(modifier: Modifier=Modifier,color: Color=NonetTheme.colorScheme.primary,strokeWidth: Dp=2.dp) {
    val turn by rememberInfiniteTransition(label="loading").animateFloat(0f,360f,infiniteRepeatable(tween(850,easing=LinearEasing)),label="turn")
    Canvas(modifier.size(24.dp).semantics {progressBarRangeInfo=ProgressBarRangeInfo.Indeterminate}) {val inset=strokeWidth.toPx()/2;drawArc(color,turn,260f,false,Offset(inset,inset),Size(size.width-inset*2,size.height-inset*2),style=Stroke(strokeWidth.toPx(),cap=StrokeCap.Round))}
}
@Composable fun LinearProgressIndicator(modifier: Modifier=Modifier,progress: (() -> Float)?=null,drawStopIndicator: (() -> Unit)?=null) {
    val c=NonetTheme.colorScheme
    // 有确定进度的播放器不启动无限动画，暂停后保持静态，避免空闲持续重绘。
    val loading=if(progress==null) {val value by rememberInfiniteTransition(label="progress").animateFloat(0f,1f,infiniteRepeatable(tween(1200,easing=LinearEasing)),label="loading-position");value} else 0f
    Canvas(modifier.fillMaxWidth().height(2.dp)) {
        drawRect(c.outlineVariant)
        if(progress!=null) drawRect(c.primary,size=Size(size.width*progress().coerceIn(0f,1f),size.height))
        else drawRect(c.primary,topLeft=Offset(size.width*(loading*1.3f-.3f),0f),size=Size(size.width*.3f,size.height))
    }
}
@Composable fun ListItem(headlineContent: @Composable ()->Unit,leadingContent: (@Composable ()->Unit)?=null,modifier: Modifier=Modifier) {
    Row(modifier.fillMaxWidth().heightIn(min=52.dp).padding(horizontal=20.dp,vertical=12.dp),horizontalArrangement=Arrangement.spacedBy(16.dp),verticalAlignment=Alignment.CenterVertically) {
        if(leadingContent!=null) leadingContent();Box(Modifier.weight(1f)) {headlineContent()}
    }
}
@Composable fun DropdownMenu(expanded: Boolean,onDismissRequest: ()->Unit,content: @Composable ColumnScope.()->Unit) {
    if(!expanded) return
    val position=remember {object : PopupPositionProvider {override fun calculatePosition(anchorBounds: IntRect,windowSize: IntSize,layoutDirection: LayoutDirection,popupContentSize: IntSize): IntOffset = IntOffset(anchorBounds.right.minus(popupContentSize.width).coerceIn(8,(windowSize.width-popupContentSize.width-8).coerceAtLeast(8)),anchorBounds.bottom.coerceAtMost((windowSize.height-popupContentSize.height-8).coerceAtLeast(8)))}}
    Popup(popupPositionProvider=position,onDismissRequest=onDismissRequest,properties=PopupProperties(focusable=true)) {
        Surface(Modifier.widthIn(min=160.dp,max=280.dp),RoundedCornerShape(12.dp),border=BorderStroke(1.dp,NonetTheme.colorScheme.outlineVariant)) {Column(Modifier.heightIn(max=320.dp).verticalScroll(rememberScrollState()).padding(6.dp),content=content)}
    }
}
@Composable fun DropdownMenuItem(text: @Composable ()->Unit,onClick: ()->Unit) {Box(Modifier.fillMaxWidth().heightIn(min=44.dp).clip(RoundedCornerShape(6.dp)).clickable(role=Role.Button,onClick=onClick).padding(horizontal=12.dp,vertical=10.dp)) {text()}}
@Composable fun AlertDialog(onDismissRequest: ()->Unit,confirmButton: @Composable ()->Unit,title: @Composable ()->Unit,text: @Composable ()->Unit,dismissButton: (@Composable ()->Unit)?=null) {
    Dialog(onDismissRequest,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.widthIn(max=520.dp).fillMaxWidth(.9f),RoundedCornerShape(18.dp)) {
            Column(Modifier.padding(22.dp),verticalArrangement=Arrangement.spacedBy(18.dp)) {
                title();Box(Modifier.heightIn(max=(LocalConfiguration.current.screenHeightDp.dp*.65f).coerceAtLeast(120.dp)).verticalScroll(rememberScrollState())) {text()}
                Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End,verticalAlignment=Alignment.CenterVertically) {if(dismissButton!=null) dismissButton();confirmButton()}
            }
        }
    }
}
@Composable fun ModalBottomSheet(onDismissRequest: ()->Unit,dragHandle: (@Composable ()->Unit)?=null,content: @Composable ColumnScope.()->Unit) {
    Dialog(onDismissRequest,properties=DialogProperties(usePlatformDefaultWidth=false,decorFitsSystemWindows=false)) {
        Box(Modifier.fillMaxSize()) {
            Box(Modifier.matchParentSize().background(Color.Black.copy(alpha=.28f)).clickable(onClick=onDismissRequest))
            Surface(Modifier.align(Alignment.BottomCenter).navigationBarsPadding().widthIn(max=720.dp).fillMaxWidth().heightIn(max=LocalConfiguration.current.screenHeightDp.dp*.86f),RoundedCornerShape(topStart=20.dp,topEnd=20.dp)) {
                Column {Box(Modifier.fillMaxWidth().height(20.dp),contentAlignment=Alignment.Center) {Box(Modifier.size(28.dp,3.dp).background(NonetTheme.colorScheme.outlineVariant,CircleShape))};content()}
            }
        }
    }
}
class SnackbarHostState {
    var message by mutableStateOf<String?>(null);private set
    private val mutex=Mutex()
    suspend fun showSnackbar(value: String)=mutex.withLock {message=value;try {delay(4000)} finally {message=null}}
}
@Composable fun SnackbarHost(state: SnackbarHostState,modifier: Modifier=Modifier) {
    AnimatedVisibility(state.message!=null,modifier,enter=fadeIn(),exit=fadeOut()) {
        Surface(shape=RoundedCornerShape(10.dp),color=Color(0xFF25272D)) {Text(state.message.orEmpty(),Modifier.padding(horizontal=18.dp,vertical=13.dp),color=Color.White)}
    }
}

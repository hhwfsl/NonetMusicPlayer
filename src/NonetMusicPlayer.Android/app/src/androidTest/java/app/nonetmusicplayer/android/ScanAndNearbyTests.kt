package app.nonetmusicplayer.android

import android.net.Uri
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.documentfile.provider.DocumentFile
import androidx.test.core.app.ApplicationProvider
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.data.*
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.*
import app.nonetmusicplayer.android.ui.*
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.MediaType.Companion.toMediaType
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID

/** 新规则使用隔离资料库和真实标签读取；连接审批使用环回 HTTPS，不访问外网。 */
@RunWith(AndroidJUnit4::class)
@androidx.media3.common.util.UnstableApi
class ScanAndNearbyTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private val p=Preferences(language="en",theme="light")
    @Test fun confirmationDoesNotScanUntilConfirmedAndHomeHasNoBrowseButton() {
        var shown by mutableStateOf(true);var scans=0
        compose.activity.setContent {MusicTheme(p) {if(shown) ScanConfirmation(ScanOptions(60),{scans++;shown=false},{shown=false},{shown=false}) else MusicHome(LibraryState(),{},{})}}
        compose.onNodeWithText("Minimum duration: 60 seconds").assertExists();assertEquals(0,scans)
        compose.onNodeWithTag("scan_cancel").performClick();assertEquals(0,scans)
        compose.onAllNodesWithText("Browse songs").assertCountEquals(0)
        compose.runOnUiThread {shown=true};compose.onNodeWithTag("scan_confirm").performClick();assertEquals(1,scans)
    }
    @Test fun remotePlaylistsAppearBelowLocalListsAndDisappearOnDisconnect() {
        var peers by mutableStateOf(listOf(NearbyDevice("peer","Test PC",listOf(NearbyPlaylist("mix","Remote mix","",emptyList())),emptyList(),"desktop")))
        var selected=""
        compose.activity.setContent {MusicTheme(p) {LibraryShell(LibraryState(),PlaybackState(),"music","Home",false,{selected=it},{},{},{},{},{},{},peers) {Box(Modifier.fillMaxSize())}}}
        compose.onNodeWithTag("navigation_toggle").performClick()
        compose.onNodeWithTag("drawer_list").performScrollToNode(hasText("Remote mix"))
        compose.onNodeWithText("Desktop · Test PC").assertIsDisplayed()
        compose.onNodeWithTag("drawer_peer:peer:mix").performClick();assertEquals("peer:peer:mix",selected)
        compose.runOnUiThread {peers=emptyList()}
        compose.onNodeWithTag("navigation_toggle").performClick()
        compose.onAllNodesWithText("Remote mix").assertCountEquals(0)
    }
    @Test fun scanRulesPersistWithoutChangingLocalPlaylists() = runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val db="scan-${UUID.randomUUID()}.db";val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO)
        val repo=MusicRepository(app,scope,db)
        val rules=ScanOptions(90,"minutes",listOf("content://com.android.externalstorage.documents/tree/primary%3AMusic"),listOf("content://com.android.externalstorage.documents/tree/primary%3AMusic%2FShort"))
        try {
            val list=repo.createPlaylist("Untouched");repo.addTracks(list,listOf(Track("one","content://fixture/one","Short song")))
            repo.updatePreferences(repo.state.value.preferences.copy(scan=rules,lanApprovalWaitSeconds=45));assertEquals(rules,repo.state.value.preferences.scan)
            repo.setPeerKind("peer","desktop");repo.addTracks(list,listOf(Track("remote","nonet-lan://peer/one","Desktop song",providerId="lan:peer")))
            val second=MusicRepository(app,scope,db)
            try {withTimeout(3000) {while(second.state.value.preferences.scan!=rules) delay(20)};assertEquals(setOf("one","remote"),second.state.value.playlists.first {it.id==list}.trackIds.toSet());assertEquals(45,second.state.value.preferences.lanApprovalWaitSeconds);assertEquals("desktop",second.state.value.tracks.first {it.id=="remote"}.sourceDeviceKind)} finally {second.close()}
        } finally {scope.cancel();repo.close();app.deleteDatabase(db)}
    }
    @Test fun folderScanPrunesExcludedSubtreesAndFiltersBeforeCreatingLyrics() = runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val root=File(app.cacheDir,"scan-${UUID.randomUUID()}").apply {mkdirs()}
        try {
            fun song(dir: File,name: String) {dir.mkdirs();File(dir,name).outputStream().use {out ->InstrumentationRegistry.getInstrumentation().context.assets.open("tone.mp3").use {it.copyTo(out)}}}
            song(root,"kept.mp3");val excluded=File(root,"Private");song(excluded,"hidden.mp3");song(File(root,"Private2"),"allowed.mp3")
            val importer=AudioImporter(app)
            val songs=importer.scanFolder(DocumentFile.fromFile(root),excluded={Uri.decode(it.path.orEmpty()).let {path ->path==excluded.absolutePath || path.startsWith(excluded.absolutePath+"/")}})
            assertEquals(2,songs.size);assertFalse(songs.any {"hidden" in it.uri})
            assertTrue(importer.scanFolder(DocumentFile.fromFile(root),minimumDurationMs=604800000).isEmpty())
            assertEquals(ScanLocation("primary","Music"),DeviceMusicScanner.location(Uri.parse("content://com.android.externalstorage.documents/tree/primary%3AMusic")))
        } finally {root.deleteRecursively()}
    }
    @Test fun mediaStoreScanAppliesActualDurationAndFolderExclusion() = runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>()
        InstrumentationRegistry.getInstrumentation().uiAutomation.grantRuntimePermission(app.packageName,android.Manifest.permission.READ_MEDIA_AUDIO)
        val resolver=app.contentResolver;val folder="Music/NonetScanTest-${UUID.randomUUID()}/";val created=mutableListOf<Uri>()
        fun audio(name: String,relative: String,seconds: Int): Uri {
            val values=android.content.ContentValues().apply {put(android.provider.MediaStore.Audio.Media.DISPLAY_NAME,name+".wav");put(android.provider.MediaStore.Audio.Media.TITLE,name);put(android.provider.MediaStore.Audio.Media.MIME_TYPE,"audio/wav");put(android.provider.MediaStore.Audio.Media.IS_MUSIC,1);put(android.provider.MediaStore.Audio.Media.DURATION,seconds*1000L);put(android.provider.MediaStore.Audio.Media.RELATIVE_PATH,relative)}
            val uri=resolver.insert(android.provider.MediaStore.Audio.Media.EXTERNAL_CONTENT_URI,values)!!;created+=uri
            val size=48000*2*seconds
            resolver.openOutputStream(uri)!!.use {out ->val writer=java.io.DataOutputStream(out)
                fun little(n: Int)=Integer.reverseBytes(n)
                fun short(n: Int)=java.lang.Short.reverseBytes(n.toShort()).toInt()
                writer.writeBytes("RIFF");writer.writeInt(little(size+36));writer.writeBytes("WAVEfmt ");writer.writeInt(little(16));writer.writeShort(short(1));writer.writeShort(short(1));writer.writeInt(little(48000));writer.writeInt(little(96000));writer.writeShort(short(2));writer.writeShort(short(16));writer.writeBytes("data");writer.writeInt(little(size));writer.write(ByteArray(size))}
            resolver.update(uri,values,null,null);return uri
        }
        try {
            val short=audio("short",folder,1);val kept=audio("kept",folder+"Excluded2/",3);val hidden=audio("hidden",folder+"Excluded/",3)
            val exclusion=android.provider.DocumentsContract.buildTreeDocumentUri("com.android.externalstorage.documents","primary:"+folder+"Excluded").toString()
            val scanner=DeviceMusicScanner(app,AudioImporter(app));val result=scanner.scan(ScanOptions(2,excludedFolders=listOf(exclusion))).map {it.uri}
            assertTrue("Allowed indexed audio missing",kept.toString() in result);assertFalse(short.toString() in result);assertFalse(hidden.toString() in result)
            val unavailable=android.provider.DocumentsContract.buildTreeDocumentUri("com.android.externalstorage.documents","primary:Missing-${UUID.randomUUID()}").toString()
            assertTrue(runCatching {scanner.scan(ScanOptions(includedFolders=listOf(unavailable)))}.isFailure)
        } finally {created.forEach {resolver.delete(it,null,null)}}
    }
    @Test fun incomingPairingRequiresAcceptAndDenialCreatesNoSession() = runBlocking {
        val (ssl,pin)=PeerSecurity.identity();val own=UUID.randomUUID().toString().replace("-","")
        var decision=CompletableDeferred<Boolean>();val captured=CompletableDeferred<IncomingConnection>();var catalogReads=0
        val server=PeerServer(own,"Phone",ssl,pin,{catalogReads++;JSONObject().put("revision","one")},{_,_->null},{throw it},{request ->captured.complete(request);decision.await()})
        try {
            val invite=PeerSecurity.parse(server.invitation("127.0.0.1"));val http=PeerSecurity.client(invite)
            fun pair()=http.newCall(Request.Builder().url(invite.origin.resolve("v1/pair")!!).post(JSONObject().put("key",invite.key).put("deviceId",UUID.randomUUID().toString().replace("-","")).put("name","Desktop test").toString().toRequestBody("application/json".toMediaType())).build()).execute()
            val pending=async(Dispatchers.IO) {pair().use {it.code}}
            assertEquals("Desktop test",withTimeout(5000) {captured.await()}.name);assertFalse(pending.isCompleted);assertEquals(0,catalogReads)
            withContext(Dispatchers.IO) {http.newCall(Request.Builder().url(invite.origin.resolve("v1/catalog")!!).build()).execute().use {assertEquals(401,it.code)}}
            decision.complete(false);assertEquals(403,pending.await())
            decision=CompletableDeferred<Boolean>().apply {complete(true)}
            withContext(Dispatchers.IO) {pair().use {assertEquals(200,it.code);assertEquals(64,JSONObject(it.body!!.string()).getString("token").length)}}
            assertEquals(0,catalogReads)
        } finally {server.close()}
    }
}

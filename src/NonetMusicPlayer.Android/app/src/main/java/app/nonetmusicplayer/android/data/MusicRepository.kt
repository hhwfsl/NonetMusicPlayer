package app.nonetmusicplayer.android.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import app.nonetmusicplayer.android.domain.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDate
import java.util.UUID

/** 唯一的数据写入入口；后台播放和界面共用同一数据库及不可变快照。 */
class MusicRepository(context: Context, private val scope: CoroutineScope, databaseName: String=if(context.getDatabasePath("littlemusicplayer.db").exists()) "littlemusicplayer.db" else "nonetmusicplayer.db") {
    private val helper = MusicDatabase(context,databaseName)
    private val filesDirectory=context.filesDir
    private val mutex = Mutex()
    private val _state = MutableStateFlow(LibraryState())
    val state = _state.asStateFlow()
    init { scope.launch { mutate {} } }
    private suspend fun mutate(statisticsOnly: Boolean = false, action: (SQLiteDatabase) -> Unit) = withContext(Dispatchers.IO) {
        mutex.withLock {
            val db = helper.writableDatabase
            db.beginTransaction()
            try { action(db); db.setTransactionSuccessful() } finally { db.endTransaction() }
            // 播放统计仅刷新小型统计集，保持曲目/歌单对象不变，避免整库重读与封面重新绑定。
            _state.value = if(statisticsOnly) {
                val stats=mutableListOf<Listening>()
                db.rawQuery("SELECT track_id,day,duration,plays FROM listening",null).use { c ->
                    while(c.moveToNext()) stats+=Listening(c.getString(0),c.getString(1),c.getLong(2),c.getInt(3))
                }
                _state.value.copy(statistics=stats)
            } else readState(db)
        }
    }
    private fun readState(db: SQLiteDatabase): LibraryState {
        val kinds=runCatching {JSONObject(readSetting(db,"deviceKinds"))}.getOrDefault(JSONObject())
        val tracks = mutableListOf<Track>()
        db.rawQuery("SELECT * FROM tracks ORDER BY rowid DESC", null).use { c -> while(c.moveToNext()) {
            tracks += Track(c.getString(0), c.getString(1), c.getString(2), c.getString(3), c.getString(4), c.getLong(5), c.getString(6), c.getString(7), c.getString(8), c.getInt(9) == 1, kinds.optString(c.getString(8),"unknown"))
        } }
        val playlists = mutableListOf<Playlist>()
        db.rawQuery("SELECT id,name,description FROM playlists ORDER BY position,rowid", null).use { c -> while(c.moveToNext()) {
            val id = c.getString(0)
            val ids = mutableListOf<String>()
            db.rawQuery("SELECT track_id FROM playlist_tracks WHERE playlist_id=? ORDER BY position", arrayOf(id)).use { m -> while(m.moveToNext()) ids += m.getString(0) }
            playlists += Playlist(id, c.getString(1), c.getString(2), ids)
        } }
        val recent = mutableListOf<String>()
        db.rawQuery("SELECT track_id FROM history ORDER BY played_at DESC", null).use { c -> while(c.moveToNext()) recent += c.getString(0) }
        val stats = mutableListOf<Listening>()
        db.rawQuery("SELECT track_id,day,duration,plays FROM listening", null).use { c -> while(c.moveToNext()) stats += Listening(c.getString(0),c.getString(1),c.getLong(2),c.getInt(3)) }
        val covers=runCatching { JSONObject(readSetting(db,"covers")) }.getOrDefault(JSONObject())
        return LibraryState(tracks=tracks,playlists=playlists,recentIds=recent,statistics=stats,preferences=preferences(readSetting(db,"preferences")),covers=covers.keys().asSequence().associateWith {covers.getString(it)})
    }
    private fun preferences(raw: String): Preferences {
        val j = runCatching { JSONObject(raw) }.getOrDefault(JSONObject())
        val scan=j.optJSONObject("scan") ?: JSONObject()
        fun folders(key: String): List<String> = scan.optJSONArray(key)?.let {a ->
            (0 until minOf(a.length(),128)).mapNotNull {a.optString(it).takeIf {v ->v.startsWith("content://com.android.externalstorage.documents/") && v.length<=4096}}.distinct()
        }.orEmpty()
        return Preferences(theme=j.optString("theme","system"),accent=j.optString("accent","#A895FF"),language=j.optString("language","system"),
            mode=j.optString("mode","repeat-all"),historyLimit=j.optInt("historyLimit",1000).coerceIn(1,10000),fontScale=j.optDouble("fontScale",1.0).toFloat().coerceIn(.85f,1.3f),
            background=j.optString("background"),backgroundOpacity=j.optDouble("backgroundOpacity",.5).toFloat().coerceIn(0f,1f),
            scan=ScanOptions(scan.optInt("minimumSeconds",0).coerceIn(0,604800),scan.optString("durationUnit","seconds").takeIf {it in listOf("seconds","minutes")} ?: "seconds",folders("includedFolders"),folders("excludedFolders")),
            lanApprovalWaitSeconds=j.optInt("lanApprovalWaitSeconds",30).coerceIn(10,120))
    }
    private fun readSetting(db: SQLiteDatabase, key: String): String = db.rawQuery("SELECT value FROM settings WHERE key=?", arrayOf(key)).use { if(it.moveToFirst()) it.getString(0) else "{}" }
    private fun putSetting(db: SQLiteDatabase, key: String, value: String) { db.execSQL("INSERT OR REPLACE INTO settings VALUES(?,?)", arrayOf(key,value)) }
    private fun putTrack(db: SQLiteDatabase, t: Track) {
        // 不使用 REPLACE：它会触发外键级联删除歌单关联和历史。
        var managed=t.inLibrary
        var lyrics=t.lyrics
        var cover=t.cover
        db.rawQuery("SELECT managed,lyrics,cover FROM tracks WHERE id=?",arrayOf(t.id)).use { existing ->
            if(existing.moveToFirst()) {
                managed=managed || existing.getInt(0)==1
                if(lyrics.isBlank()) lyrics=existing.getString(1)
                if(t.providerId.startsWith("lan:") && java.io.File(existing.getString(2)).isFile) cover=existing.getString(2)
            }
        }
        val values = ContentValues().apply {
            put("id",t.id);put("uri",t.uri);put("title",t.title);put("artist",t.artist);put("album",t.album);put("duration",t.durationMs)
            put("cover",cover);put("lyrics",lyrics);put("provider",t.providerId);put("managed",if(managed) 1 else 0)
        }
        if(db.update("tracks",values,"id=?",arrayOf(t.id)) == 0) db.insertOrThrow("tracks",null,values)
    }
    suspend fun storeTrack(track: Track) = mutate { db -> putTrack(db,track) }
    /** 目录刷新只发布一次快照，避免逐首读取整个资料库造成平方级开销。 */
    suspend fun storeTracks(tracks: List<Track>) = mutate { db -> tracks.distinctBy {it.id}.forEach {putTrack(db,it)} }
    suspend fun addTracks(playlistId: String, tracks: List<Track>) = mutate { db ->
        require(db.rawQuery("SELECT 1 FROM playlists WHERE id=?",arrayOf(playlistId)).use { it.moveToFirst() })
        val incoming=tracks.distinctBy {it.id}
        val addedIds=incoming.map {it.id}.toSet()
        val previous=mutableListOf<String>()
        db.rawQuery("SELECT track_id FROM playlist_tracks WHERE playlist_id=? ORDER BY position",arrayOf(playlistId)).use { c ->
            while(c.moveToNext()) if(c.getString(0) !in addedIds) previous+=c.getString(0)
        }
        incoming.forEach {putTrack(db,it.copy(inLibrary=true))}
        db.delete("playlist_tracks","playlist_id=?",arrayOf(playlistId))
        db.compileStatement("INSERT INTO playlist_tracks VALUES(?,?,?)").use { statement ->
            (incoming.asReversed().map {it.id}+previous).forEachIndexed { index,id ->
                statement.bindString(1,playlistId);statement.bindString(2,id);statement.bindLong(3,index.toLong());statement.executeInsert()
            }
        }
    }
    suspend fun createPlaylist(name: String): String {
        require(name.trim().isNotEmpty() && name.length <= 100)
        val id=UUID.randomUUID().toString()
        mutate { it.execSQL("INSERT INTO playlists VALUES(?,?,?,(SELECT COALESCE(MAX(position),0)+1 FROM playlists))",arrayOf(id,name.trim(),"")) }
        return id
    }
    suspend fun editPlaylist(id: String, name: String, description: String) = mutate {
        require(id != "liked" && name.trim().isNotEmpty() && name.length <= 100 && description.length <= 2000)
        it.execSQL("UPDATE playlists SET name=?,description=? WHERE id=?",arrayOf(name.trim(),description,id))
    }
    suspend fun deletePlaylist(id: String) = mutate { require(id != "liked"); it.delete("playlists","id=?",arrayOf(id));markUnreferenced(it) }
    suspend fun removeTrack(playlistId: String, trackId: String) = mutate { it.delete("playlist_tracks","playlist_id=? AND track_id=?",arrayOf(playlistId,trackId));markUnreferenced(it) }
    suspend fun moveTrack(playlistId: String, trackId: String, delta: Int) = mutate { db ->
        val ids=_state.value.playlists.firstOrNull { it.id==playlistId }?.trackIds?.toMutableList() ?: return@mutate
        val old=ids.indexOf(trackId); if(old < 0) return@mutate
        ids.removeAt(old);ids.add((old+delta).coerceIn(0,ids.size),trackId)
        ids.forEachIndexed { i,id -> db.execSQL("UPDATE playlist_tracks SET position=? WHERE playlist_id=? AND track_id=?",arrayOf<Any>(i,playlistId,id)) }
    }
    suspend fun toggleFavorite(track: Track) {
        if(_state.value.playlists.first { it.id=="liked" }.trackIds.contains(track.id)) removeTrack("liked",track.id)
        else addTracks("liked", listOf(track))
    }
    suspend fun updatePreferences(p: Preferences) = mutate { db ->
        require(p.lanApprovalWaitSeconds in 10..120)
        require(p.theme in listOf("system","light","dark") && Regex("#[0-9a-fA-F]{6}").matches(p.accent))
        require(p.backgroundOpacity in 0f..1f)
        require(p.historyLimit in 1..10000 && p.fontScale in .85f..1.3f && p.mode in listOf("repeat-all","repeat-one","shuffle"))
        require(p.language in listOf("system","zh","en","ja"))
        require(p.scan.minimumSeconds in 0..604800 && p.scan.durationUnit in listOf("seconds","minutes"))
        require((p.scan.includedFolders+p.scan.excludedFolders).let {it.size<=256 && it.all {uri ->uri.startsWith("content://com.android.externalstorage.documents/") && uri.length<=4096}})
        val scan=JSONObject().put("minimumSeconds",p.scan.minimumSeconds).put("durationUnit",p.scan.durationUnit)
            .put("includedFolders",JSONArray(p.scan.includedFolders.distinct())).put("excludedFolders",JSONArray(p.scan.excludedFolders.distinct()))
        putSetting(db,"preferences",JSONObject().put("theme",p.theme).put("accent",p.accent).put("language",p.language).put("mode",p.mode).put("historyLimit",p.historyLimit).put("fontScale",p.fontScale).put("background",p.background).put("backgroundOpacity",p.backgroundOpacity).put("scan",scan).put("lanApprovalWaitSeconds",p.lanApprovalWaitSeconds).toString())
        db.execSQL("DELETE FROM history WHERE track_id NOT IN (SELECT track_id FROM history ORDER BY played_at DESC LIMIT ?)",arrayOf(p.historyLimit))
    }
    suspend fun recordStarted(trackId: String) = mutate { db ->
        val track=_state.value.tracks.firstOrNull { it.id==trackId } ?: return@mutate
        db.execSQL("INSERT OR REPLACE INTO history VALUES(?,?)",arrayOf<Any>(trackId,System.currentTimeMillis()))
        db.execSQL("DELETE FROM history WHERE track_id NOT IN (SELECT track_id FROM history ORDER BY played_at DESC LIMIT ?)",arrayOf(_state.value.preferences.historyLimit))
        if(track.inLibrary) {
            val day=LocalDate.now().toString()
            db.execSQL("INSERT OR IGNORE INTO listening VALUES(?,?,0,0)",arrayOf(trackId,day))
            db.execSQL("UPDATE listening SET plays=plays+1 WHERE track_id=? AND day=?",arrayOf(trackId,day))
        }
    }
    suspend fun recordDuration(trackId: String, elapsedMs: Long) = mutate(statisticsOnly=true) { db ->
        if(_state.value.tracks.none { it.id==trackId && it.inLibrary }) return@mutate
        val day=LocalDate.now().toString()
        db.execSQL("INSERT OR IGNORE INTO listening VALUES(?,?,0,0)",arrayOf(trackId,day))
        db.execSQL("UPDATE listening SET duration=duration+? WHERE track_id=? AND day=?",arrayOf<Any>(elapsedMs.coerceIn(0,10000),trackId,day))
    }
    suspend fun savePlayback(snapshot: PlaybackSnapshot) = withContext(Dispatchers.IO) { mutex.withLock {
        putSetting(helper.writableDatabase,"playback",JSONObject().put("trackId",snapshot.trackId).put("queue",JSONArray(snapshot.queueIds)).put("position",snapshot.positionMs).put("volume",snapshot.volume).put("source",snapshot.source).toString())
    } }
    suspend fun restorePlayback(): PlaybackSnapshot = withContext(Dispatchers.IO) { mutex.withLock {
        val j=runCatching { JSONObject(readSetting(helper.readableDatabase,"playback")) }.getOrDefault(JSONObject())
        val ids=j.optJSONArray("queue") ?: JSONArray()
        PlaybackSnapshot(j.optString("trackId"),List(ids.length()){ids.getString(it)},j.optLong("position").coerceAtLeast(0),j.optDouble("volume",1.0).toFloat().coerceIn(0f,1f),j.optString("source"))
    } }
    suspend fun setLyrics(trackId: String, path: String) {
        val previous=_state.value.tracks.firstOrNull {it.id==trackId}?.lyrics.orEmpty()
        mutate {db ->db.execSQL("UPDATE tracks SET lyrics=? WHERE id=?",arrayOf(path,trackId))}
        // 每次导入生成新路径来失效歌词缓存；只清理本应用 Lyrics 内的旧副本。
        withContext(Dispatchers.IO) {
            if(previous!=path && previous.isNotBlank()) {
                val file=java.io.File(previous)
                if(file.isAbsolute && file.canonicalPath.startsWith(java.io.File(filesDirectory,"Lyrics").canonicalPath+java.io.File.separator)) file.delete()
            }
        }
    }
    suspend fun clearHistory() = mutate { it.delete("history",null,null) }
    suspend fun removeRecent(ids: Set<String>) = mutate { db ->
        ids.forEach {db.delete("history","track_id=?",arrayOf(it))}
    }
    /** 断线清除临时设备曲库；本机歌单与收藏引用保持不变。 */
    suspend fun removePeerEphemeral(peer: String?=null) = mutate {db ->
        if(peer==null) db.execSQL("DELETE FROM tracks WHERE provider LIKE 'lan:%' AND id NOT IN (SELECT track_id FROM playlist_tracks)")
        else db.execSQL("DELETE FROM tracks WHERE provider=? AND id NOT IN (SELECT track_id FROM playlist_tracks)",arrayOf("lan:$peer"))
    }
    /** 设备类型随引用保存，断线后仍能正确区分桌面歌曲和移动歌曲，不改变旧表宽度。 */
    suspend fun setPeerKind(id: String,kind: String) = mutate {db ->
        val kinds=runCatching {JSONObject(readSetting(db,"deviceKinds"))}.getOrDefault(JSONObject())
        kinds.put("lan:$id",kind.takeIf {it in listOf("desktop","mobile")} ?: "unknown");putSetting(db,"deviceKinds",kinds.toString())
    }
    suspend fun setCover(key: String, path: String) = mutate { db ->
        val covers=runCatching {JSONObject(readSetting(db,"covers"))}.getOrDefault(JSONObject())
        if(path.isBlank()) covers.remove(key) else covers.put(key,path)
        putSetting(db,"covers",covers.toString())
    }
    suspend fun movePlaylist(id: String, delta: Int) = mutate { db ->
        val ids=_state.value.playlists.map {it.id}.toMutableList()
        val old=ids.indexOf(id);if(old<0) return@mutate
        ids.removeAt(old);ids.add((old+delta).coerceIn(0,ids.size),id)
        ids.forEachIndexed {i,key ->db.execSQL("UPDATE playlists SET position=? WHERE id=?",arrayOf<Any>(i,key))}
    }
    suspend fun removeTracks(playlist: String, ids: Set<String>) = mutate { db ->
        ids.forEach {db.delete("playlist_tracks","playlist_id=? AND track_id=?",arrayOf(playlist,it))}
        markUnreferenced(db)
    }
    /** 移除软件引用，不触碰 content URI 所指向的用户音频或源歌词。 */
    suspend fun removeEverywhere(ids: Set<String>) = mutate { db ->
        ids.forEach {db.delete("playlist_tracks","track_id=?",arrayOf(it))}
        markUnreferenced(db)
    }
    private fun markUnreferenced(db: SQLiteDatabase) {
        db.execSQL("UPDATE tracks SET managed=0 WHERE id NOT IN (SELECT track_id FROM playlist_tracks)")
    }
    /** 备份仅包含软件元数据，不包含音频或 Keystore 中的认证令牌。 */
    suspend fun exportMetadata(): JSONObject = withContext(Dispatchers.IO) {mutex.withLock {
        val result=JSONObject().put("schema",1)
        val tables=JSONObject()
        listOf("tracks","playlists","playlist_tracks","history","listening","settings").forEach {name ->
            val rows=JSONArray()
            helper.readableDatabase.rawQuery("SELECT * FROM $name",null).use {c ->
                while(c.moveToNext()) {
                    val row=JSONArray();for(i in 0 until c.columnCount) row.put(c.getString(i));rows.put(row)
                }
            }
            tables.put(name,rows)
        }
        result.put("tables",tables)
    }}
    suspend fun restoreMetadata(json: JSONObject) {
        require(json.getInt("schema")==1)
        val widths=linkedMapOf("tracks" to 10,"playlists" to 4,"playlist_tracks" to 3,"history" to 2,"listening" to 4,"settings" to 2)
        val tables=json.getJSONObject("tables")
        widths.forEach {(name,width) ->
            val rows=tables.getJSONArray(name);require(rows.length()<=100000)
            for(i in 0 until rows.length()) require(rows.getJSONArray(i).length()==width)
        }
        mutate {db ->
            widths.keys.reversed().forEach {db.delete(it,null,null)}
            widths.forEach {(name,width) ->
                val rows=tables.getJSONArray(name)
                db.compileStatement("INSERT INTO $name VALUES("+List(width) {"?"}.joinToString(",")+")").use {stmt ->
                    for(i in 0 until rows.length()) {
                        val row=rows.getJSONArray(i)
                        for(j in 0 until width) stmt.bindString(j+1,row.getString(j))
                        stmt.executeInsert()
                    }
                }
            }
            require(db.rawQuery("SELECT 1 FROM playlists WHERE id='liked'",null).use {it.moveToFirst()})
            putSetting(db,"playback","{}")
        }
    }
    fun close() { helper.close() }
}

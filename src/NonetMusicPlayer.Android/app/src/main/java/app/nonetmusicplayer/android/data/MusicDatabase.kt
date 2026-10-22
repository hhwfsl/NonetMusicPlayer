package app.nonetmusicplayer.android.data

import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper

/** 使用外键和事务维护歌单次序；删歌单不删除音频本体。 */
class MusicDatabase(context: Context, name: String = "nonetmusicplayer.db") : SQLiteOpenHelper(context, name, null, 1) {
    override fun onConfigure(db: SQLiteDatabase) { db.setForeignKeyConstraintsEnabled(true); db.enableWriteAheadLogging() }
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("CREATE TABLE tracks(id TEXT PRIMARY KEY,uri TEXT NOT NULL,title TEXT NOT NULL,artist TEXT NOT NULL,album TEXT NOT NULL,duration INTEGER NOT NULL,cover TEXT NOT NULL,lyrics TEXT NOT NULL,provider TEXT NOT NULL,managed INTEGER NOT NULL)")
        db.execSQL("CREATE TABLE playlists(id TEXT PRIMARY KEY,name TEXT NOT NULL,description TEXT NOT NULL,position INTEGER NOT NULL)")
        db.execSQL("CREATE TABLE playlist_tracks(playlist_id TEXT REFERENCES playlists(id) ON DELETE CASCADE,track_id TEXT REFERENCES tracks(id) ON DELETE CASCADE,position INTEGER NOT NULL,PRIMARY KEY(playlist_id,track_id))")
        db.execSQL("CREATE TABLE history(track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,played_at INTEGER NOT NULL)")
        db.execSQL("CREATE TABLE listening(track_id TEXT REFERENCES tracks(id) ON DELETE CASCADE,day TEXT NOT NULL,duration INTEGER NOT NULL,plays INTEGER NOT NULL,PRIMARY KEY(track_id,day))")
        db.execSQL("CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL)")
        db.execSQL("INSERT INTO playlists VALUES('liked','','',0)")
    }
    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) { error("Unsupported database upgrade: $oldVersion → $newVersion") }
}

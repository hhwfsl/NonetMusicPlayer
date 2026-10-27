package app.nonetmusicplayer.android

import android.database.sqlite.SQLiteDatabase
import androidx.test.core.app.ApplicationProvider
import app.nonetmusicplayer.android.data.MusicDatabase
import org.junit.Assert.*
import org.junit.Test

class RemovedSourceMigrationTests {
    @Test fun versionTwoUpgradeKeepsLocalMusicAndListeningHistory() {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val name="migration-"+java.util.UUID.randomUUID()+".db"
        try {
            val helper=MusicDatabase(app,name);val db=helper.writableDatabase
            db.execSQL("INSERT INTO tracks VALUES('local','content://test','Local','','',1,'','','',1)")
            db.execSQL("INSERT INTO tracks VALUES('old','nonet://netease/1','Old','','',1,'','','netease',1)")
            db.execSQL("INSERT INTO playlist_tracks VALUES('liked','local',0),('liked','old',1)")
            db.execSQL("INSERT INTO history VALUES('old',1)")
            db.execSQL("INSERT INTO listening VALUES('old','2026-10-27',12345,1)")
            db.execSQL("INSERT INTO listening_hours VALUES('old',1,12345,1)")
            db.version=2;helper.close()
            val upgraded=MusicDatabase(app,name);upgraded.readableDatabase.use {result ->
                result.rawQuery("SELECT track_id FROM playlist_tracks",null).use {assertTrue(it.moveToFirst());assertEquals("local",it.getString(0));assertFalse(it.moveToNext())}
                result.rawQuery("SELECT SUM(duration) FROM listening",null).use {it.moveToFirst();assertEquals(12345,it.getInt(0))}
                result.rawQuery("SELECT SUM(duration) FROM listening_hours",null).use {it.moveToFirst();assertEquals(12345,it.getInt(0))}
                result.rawQuery("SELECT count(*) FROM history",null).use {it.moveToFirst();assertEquals(0,it.getInt(0))}
                assertEquals(3,result.version)
            };upgraded.close()
        } finally {app.deleteDatabase(name)}
    }
}

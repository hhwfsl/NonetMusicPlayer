package app.nonetmusicplayer.android.domain

/** 平台独立的曲目引用；content URI 由安卓授权管理，不转成不可靠的文件路径。 */
data class Track(
    val id: String, val uri: String, val title: String, val artist: String = "", val album: String = "",
    val durationMs: Long = 0, val cover: String = "", val lyrics: String = "", val providerId: String = "", val inLibrary: Boolean = true,
    val sourceDeviceKind: String = "unknown",
)
data class Playlist(val id: String, val name: String, val description: String = "", val trackIds: List<String> = emptyList())
data class Listening(val trackId: String, val day: String, val durationMs: Long, val plays: Int)

/** 小时键使用 UTC，展示时才转换到本地时间，避免夏令时重复小时覆盖。 */
data class HourlyListening(val utcHour: Long, val durationMs: Long, val plays: Int)
data class Preferences(
    val theme: String = "system", val accent: String = "#A895FF", val language: String = "system",
    val mode: String = "repeat-all", val historyLimit: Int = 1000, val fontScale: Float = 1f,
    val background: String = "", val backgroundOpacity: Float = .5f,
    val scan: ScanOptions = ScanOptions(),
    val lanApprovalWaitSeconds: Int = 30,
    val sleepMinutes: Int = 30, val sleepFinishSong: Boolean = false,
)
data class LibraryState(
    val tracks: List<Track> = emptyList(), val playlists: List<Playlist> = listOf(Playlist("liked", "")),
    val covers: Map<String,String> = emptyMap(),
    val recentIds: List<String> = emptyList(), val statistics: List<Listening> = emptyList(), val preferences: Preferences = Preferences(),
    val hourlyListening: List<HourlyListening> = emptyList(),
)
data class PlaybackSnapshot(
    val trackId: String = "", val queueIds: List<String> = emptyList(), val positionMs: Long = 0, val volume: Float = 1f, val source: String = "",
)
data class PlaybackState(
    val track: Track? = null, val positionMs: Long = 0, val durationMs: Long = 0,
    val playing: Boolean = false, val buffering: Boolean = false, val ready: Boolean = false, val source: String = "",
)

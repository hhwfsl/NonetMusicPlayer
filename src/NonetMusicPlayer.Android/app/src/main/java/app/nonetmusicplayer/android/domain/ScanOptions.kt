package app.nonetmusicplayer.android.domain

/** 扫描只增加引用，不因过滤规则改变而删除已经导入的歌曲。 */
data class ScanOptions(
    val minimumSeconds: Int = 0,
    val durationUnit: String = "seconds",
    val includedFolders: List<String> = emptyList(),
    val excludedFolders: List<String> = emptyList(),
) {
    fun acceptsDuration(milliseconds: Long) = minimumSeconds == 0 || milliseconds >= minimumSeconds * 1000L
}

/** 用卷标和路径段比较目录，避免 Music 错误匹配 Music2，或不同存储卷互相排除。 */
data class ScanLocation(val volume: String, val path: String) {
    fun contains(other: ScanLocation): Boolean = volume.equals(other.volume,true) &&
        (path.isEmpty() || path == other.path || other.path.startsWith("$path/"))
    companion object {
        fun documentId(id: String): ScanLocation? {
            if(':' !in id) return null
            val volume=id.substringBefore(':').lowercase().replace("external_primary","primary")
            val parts=id.substringAfter(':').trim('/').split('/').filter {it.isNotEmpty()}
            if(volume.isBlank() || parts.any {it==".." || it=="."}) return null
            return ScanLocation(volume,parts.joinToString("/"))
        }
    }
}

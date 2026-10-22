package app.nonetmusicplayer.android.updates

/** 不按字符串或发布时间排序，保证 beta.10 正确排在 beta.9 后面。 */
internal data class UpdateVersion(val numbers: List<Int>,val preview: List<String>): Comparable<UpdateVersion> {
    override fun compareTo(other: UpdateVersion): Int {
        numbers.zip(other.numbers).forEach {(a,b)->if(a!=b) return a.compareTo(b)}
        if(preview.isEmpty()!=other.preview.isEmpty()) return if(preview.isEmpty()) 1 else -1
        for(i in 0 until minOf(preview.size,other.preview.size)) {
            val a=preview[i];val b=other.preview[i];val an=a.toIntOrNull();val bn=b.toIntOrNull()
            val result=when {an!=null && bn!=null ->an.compareTo(bn);an!=null ->-1;bn!=null ->1;else ->a.compareTo(b)}
            if(result!=0) return result
        }
        return preview.size.compareTo(other.preview.size)
    }
    companion object {
        fun parse(text: String): UpdateVersion? {
            val m=Regex("^(\\d+)\\.(\\d+)\\.(\\d+)(?:-([0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?(?:\\+[0-9A-Za-z.-]+)?$").matchEntire(text) ?: return null
            val numbers=(1..3).map {m.groupValues[it].toIntOrNull() ?: return null}
            return UpdateVersion(numbers,m.groupValues[4].takeIf {it.isNotEmpty()}?.split('.') ?: emptyList())
        }
    }
}

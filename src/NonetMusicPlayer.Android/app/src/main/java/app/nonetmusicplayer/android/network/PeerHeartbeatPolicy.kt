package app.nonetmusicplayer.android.network

/** 心跳只判断可达性，不决定授权寿命；系统停网期间保留配对并有界退避重试。 */
internal class PeerHeartbeatPolicy {
    private var firstFailure: Long?=null
    private var failures=0
    @Volatile private var authenticatedContact=Long.MIN_VALUE
    @Volatile var reachable=true;private set
    val delayMs: Long get()=if(failures==0) 5000 else (1000L shl failures.coerceAtMost(4)).coerceAtMost(15000)
    fun succeeded() {firstFailure=null;failures=0;reachable=true}
    fun contact(elapsedMs: Long) {authenticatedContact=elapsedMs;reachable=true}
    fun unavailable() {reachable=false;failures=failures.coerceAtLeast(2)}
    fun failed(elapsedMs: Long): Boolean {
        if(firstFailure==null) firstFailure=elapsedMs
        failures=(failures+1).coerceAtMost(8)
        val activeAt=maxOf(firstFailure!!,authenticatedContact)
        if(failures>=2 && elapsedMs-activeAt>=10_000) reachable=false
        return !reachable
    }
}

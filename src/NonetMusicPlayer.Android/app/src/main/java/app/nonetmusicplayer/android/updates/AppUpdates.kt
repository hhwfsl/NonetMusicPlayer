package app.nonetmusicplayer.android.updates

import android.content.*
import android.content.pm.PackageManager
import android.net.Uri
import androidx.core.content.FileProvider
import app.nonetmusicplayer.android.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest
import java.util.UUID

internal enum class UpdatePhase {Idle,Checking,Available,Downloading,Verifying,Ready,Cancelling,Current,Error}
internal data class UpdateState(val phase: UpdatePhase=UpdatePhase.Idle,val release: AndroidRelease?=null,val received: Long=0,val dialog: Boolean=false,val install: Boolean=false,val error: Int=0,val transferId: String="") {
    val active get()=phase in setOf(UpdatePhase.Downloading,UpdatePhase.Verifying,UpdatePhase.Cancelling)
}
internal fun interface ApkValidation {fun validate(file: File,release: AndroidRelease)}

/** 安装前校验包身份、单调版本与当前签名；最终安装仍交给 Android，由用户确认。 */
internal class InstalledApkValidation(private val app: Context): ApkValidation {
    override fun validate(file: File,release: AndroidRelease) {
        val flags=PackageManager.GET_SIGNING_CERTIFICATES
        val target=app.packageManager.getPackageArchiveInfo(file.path,flags) ?: throw UpdateFailure(R.string.update_invalid_package)
        val current=app.packageManager.getPackageInfo(app.packageName,flags)
        fun signatures(p: android.content.pm.PackageInfo)=p.signingInfo?.apkContentsSigners?.map {MessageDigest.getInstance("SHA-256").digest(it.toByteArray()).toList()}?.toSet().orEmpty()
        if(target.packageName!=app.packageName || target.longVersionCode<=current.longVersionCode || target.versionName!=release.version ||
            target.applicationInfo?.minSdkVersion?.let {it>android.os.Build.VERSION.SDK_INT}==true || signatures(target).isEmpty() || signatures(target)!=signatures(current)) throw UpdateFailure(R.string.update_invalid_package)
    }
}

/** 应用级更新会话：页面切换不重建下载，不保存 Activity，不触碰音乐及用户数据库。 */
internal class AppUpdates(private val app: MusicApplication,private val source: UpdateSource=GithubUpdateSource(),private val transfer: ApkTransfer=HttpsApkTransfer(),private val validation: ApkValidation=InstalledApkValidation(app)) {
    private val mutable=MutableStateFlow(UpdateState());val state=mutable.asStateFlow()
    private val folder=File(app.cacheDir,"app-updates")
    private val partial=File(folder,"pending.part");val apk=File(folder,"apk/verified.apk")
    private val record=File(folder,"ready.json")
    private var checking: Job?=null;private var downloading: Job?=null
    private var launchCheckRequested=false
    private val initialized=app.scope.launch {
        val saved=withContext(Dispatchers.IO) {
            folder.mkdirs();partial.delete()
            runCatching {
                if(!record.exists()) {apk.delete();return@runCatching null}
                if(record.length()>70000) error("Invalid update record")
                val r=JSONObject(record.readText());val release=AndroidRelease(r.getString("version"),r.getString("notes"),r.getString("url"),r.getLong("size"),r.getString("sha256"))
                if(!GithubUpdateSource.validAssetUrl(release.url) || !apk.isFile || apk.length()!=release.size || release.sha256!=apk.inputStream().use {input ->
                        MessageDigest.getInstance("SHA-256").run {val buffer=ByteArray(65536);while(true){val n=input.read(buffer);if(n<0) break;update(buffer,0,n)};digest().joinToString("") {"%02x".format(it)}}}) error("Invalid update cache")
                validation.validate(apk,release);release
            }.getOrElse {apk.delete();record.delete();null}
        }
        // 进程重建后没有旧网络任务；清除遗留进度通知，只恢复已验证的安装包。
        UpdateNotifications.clear(app)
        if(saved!=null) {mutable.value=UpdateState(UpdatePhase.Ready,saved,received=saved.size);UpdateNotifications.post(app,state.value)}
    }
    fun checkAfterCreate(restoredActivity: Boolean) {if(!restoredActivity || !launchCheckRequested) checkOnLaunch()}
    fun checkOnLaunch() {launchCheckRequested=true;check(true)}
    fun check(automatic: Boolean=false) {
        if(checking?.isActive==true || state.value.active) return
        checking=app.scope.launch {
            initialized.join();if(state.value.phase==UpdatePhase.Ready){if(!automatic) present();return@launch}
            mutable.update {it.copy(phase=UpdatePhase.Checking,dialog=!automatic,error=0)}
            try {
                val release=source.latest(BuildConfig.VERSION_NAME)
                mutable.value=UpdateState(if(release==null) UpdatePhase.Current else UpdatePhase.Available,release,dialog=!automatic || release!=null)
                app.log.write("INFO","Android update check completed")
            } catch(e: CancellationException){throw e}
            catch(e: Exception) {app.log.write("WARN","Android update check failed",e);mutable.value=UpdateState(UpdatePhase.Error,dialog=!automatic,error=(e as? UpdateFailure)?.text ?: R.string.update_check_failed)}
        }
    }
    fun present(install: Boolean=false) {app.scope.launch {initialized.join();mutable.update {it.copy(dialog=true,install=install && it.phase==UpdatePhase.Ready)}}}
    fun background() {mutable.update {it.copy(dialog=false)}}
    fun dismiss() {mutable.update {it.copy(dialog=false,install=false)}}
    fun requestInstall() {mutable.update {it.copy(install=it.phase==UpdatePhase.Ready,dialog=true)}}
    fun consumeInstall() {mutable.update {it.copy(install=false)}}
    fun installationFailed(error: Int) {mutable.update {it.copy(install=false,error=error,dialog=true)}}
    fun begin(background: Boolean) {
        val before=state.value
        if(before.phase!=UpdatePhase.Available || before.release==null) return
        val id=UUID.randomUUID().toString()
        mutable.value=before.copy(phase=UpdatePhase.Downloading,received=0,dialog=!background,transferId=id,error=0)
        try {androidx.core.content.ContextCompat.startForegroundService(app,Intent(app,UpdateDownloadService::class.java).setAction(UpdateDownloadService.START).putExtra("transfer",id))}
        catch(e: Exception){app.log.write("ERROR","Update foreground service could not start",e);mutable.value=before.copy(phase=UpdatePhase.Error,error=R.string.update_download_failed,dialog=true)}
    }
    /** 仅服务执行此方法；单次网络任务有唯一身份，旧通知不能影响后续下载。 */
    fun runTransfer(id: String,finished: (UpdateState)->Unit) {
        if(state.value.transferId!=id || state.value.phase!=UpdatePhase.Downloading || downloading?.isActive==true) {finished(state.value);return}
        val release=requireNotNull(state.value.release)
        downloading=app.scope.launch {
            try {
                initialized.join()
                withContext(Dispatchers.IO) {
                    folder.mkdirs();apk.parentFile?.mkdirs();partial.delete();apk.delete();record.delete()
                    transfer.fetch(release,partial) {bytes ->mutable.update {if(it.transferId==id) it.copy(received=bytes) else it}}
                    ensureActive();mutable.update {it.copy(phase=UpdatePhase.Verifying)}
                    validation.validate(partial,release);ensureActive()
                    if(!partial.renameTo(apk)) error("APK commit failed")
                    android.util.AtomicFile(record).apply {val output=startWrite();try {output.write(release.json().toString().toByteArray());finishWrite(output)}catch(e: Exception){failWrite(output);throw e}}
                }
                ensureActive();mutable.update {it.copy(phase=UpdatePhase.Ready,received=release.size,install=true)}
                app.log.write("INFO","Android update downloaded and verified")
            } catch(e: CancellationException) {
                // 清理及状态复位属于同一不可取消段，避免切回已取消的调度器时跳过复位。
                withContext(NonCancellable) {withContext(Dispatchers.IO) {cleanFiles()};mutable.value=UpdateState();app.messages.tryEmit(R.string.update_cancelled)}
            } catch(e: Exception) {
                val cancelled=!currentCoroutineContext().isActive
                withContext(NonCancellable) {
                    withContext(Dispatchers.IO){cleanFiles()}
                    if(cancelled) {mutable.value=UpdateState();app.messages.tryEmit(R.string.update_cancelled)}
                    else {app.log.write("ERROR","Android update download failed",e);mutable.update {it.copy(phase=UpdatePhase.Error,error=(e as? UpdateFailure)?.text ?: R.string.update_download_failed,install=false)}}
                }
            } finally {transfer.cancel();downloading=null;finished(state.value)}
        }
    }
    fun cancel(id: String=state.value.transferId) {
        if(id!=state.value.transferId) return
        if(state.value.active) {
            mutable.update {it.copy(phase=UpdatePhase.Cancelling,install=false)};transfer.cancel()
            if(downloading!=null) downloading?.cancel() else app.scope.launch {initialized.join();withContext(Dispatchers.IO){cleanFiles()};mutable.value=UpdateState()}
        } else if(state.value.phase==UpdatePhase.Ready) app.scope.launch {
            withContext(Dispatchers.IO){cleanFiles()};mutable.value=UpdateState();UpdateNotifications.clear(app)
        }
    }
    private fun cleanFiles() {partial.delete();apk.delete();record.delete();File(folder,"ready.json.bak").delete();File(folder,"ready.json.new").delete()}
    fun installIntent(): Intent {
        check(state.value.phase==UpdatePhase.Ready && apk.isFile)
        return Intent(Intent.ACTION_VIEW).setDataAndType(FileProvider.getUriForFile(app,app.packageName+".updates",apk),"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    }
}

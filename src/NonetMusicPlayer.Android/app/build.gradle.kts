plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}
android {
    namespace = "app.nonetmusicplayer.android"
    compileSdk = 36
    defaultConfig {
        // 保留已发布包的应用 ID，允许覆盖升级并保留用户授权和资料库。
        applicationId = "app.littlemusicplayer.android"
        minSdk = 28
        targetSdk = 36
        versionCode = 11
        versionName = "0.3.0-beta.9"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }
    buildFeatures { compose = true; buildConfig = true }
    bundle { language { enableSplit = false } }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    signingConfigs.getByName("debug") {
        providers.gradleProperty("nonetSigningStore").orNull?.let { storeFile = file(it) }
    }
    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            // 本地预发布使用测试签名，便于直接安装；商店发行需独立保管正式签名。
            signingConfig = signingConfigs.getByName("debug")
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }
    packaging { resources.excludes += setOf("META-INF/AL2.0", "META-INF/LGPL2.1", "META-INF/LICENSE.md", "META-INF/LICENSE-notice.md") }
}
// 将项目与新增矢量资源许可一起带入 APK，来源文档仍只维护一份。
val nonetLicenses by tasks.registering(Sync::class) {
    from(rootProject.file("LICENSE"))
    from(rootProject.file("THIRD-PARTY-NOTICES.md"))
    from(rootProject.file("docs/licenses"))
    into(layout.buildDirectory.dir("generated/nonetLicenses/licenses"))
}
android.sourceSets.getByName("main").assets.srcDir(layout.buildDirectory.dir("generated/nonetLicenses"))
tasks.named("preBuild").configure {dependsOn(nonetLicenses)}
dependencies {
    implementation(platform("androidx.compose:compose-bom:2025.05.01"))
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.core:core-ktx:1.16.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.9.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.9.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.documentfile:documentfile:1.0.1")
    implementation("androidx.media3:media3-exoplayer:1.6.1")
    implementation("androidx.media3:media3-session:1.6.1")
    implementation("androidx.media3:media3-datasource-okhttp:1.6.1")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("io.coil-kt:coil-compose:2.7.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    testImplementation("junit:junit:4.13.2")
    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
    androidTestImplementation("androidx.test:runner:1.6.1")
    androidTestImplementation("androidx.compose.ui:ui-test-junit4:1.8.2")
}

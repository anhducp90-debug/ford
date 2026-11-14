plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.inkboard"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.inkboard.app"
        minSdk = 29
        targetSdk = 35
        versionCode = 3
        versionName = "0.3.0"
        ndk { abiFilters += listOf("arm64-v8a") }
    }

    signingConfigs {
        // Sideload key for personal testing only (lets new APKs install over old ones).
        // Google Play releases will use a separate private upload key.
        create("sideload") {
            storeFile = file("inkboard-sideload.jks")
            storePassword = "inkboard-sideload"
            keyAlias = "inkboard"
            keyPassword = "inkboard-sideload"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.getByName("sideload")
        }
        debug {
            signingConfig = signingConfigs.getByName("sideload")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }
    packaging { resources.excludes += "/META-INF/{AL2.0,LGPL2.1}" }
    testOptions { unitTests.isIncludeAndroidResources = true }
}

// Build the Rust core (libinkcore.so) with cargo-ndk before packaging.
val ndkDir = android.sdkDirectory.resolve("ndk/27.2.12479018")
val buildRustCore by tasks.registering(Exec::class) {
    workingDir = rootProject.file("core")
    environment("ANDROID_NDK_HOME", ndkDir.absolutePath)
    commandLine(
        System.getProperty("user.home") + "/.cargo/bin/cargo",
        "ndk", "-t", "arm64-v8a", "-P", "29",
        "-o", project.file("src/main/jniLibs").absolutePath,
        "build", "--release"
    )
    inputs.dir(rootProject.file("core/src"))
    inputs.file(rootProject.file("core/Cargo.toml"))
    outputs.dir(project.file("src/main/jniLibs"))
}
tasks.named("preBuild") { dependsOn(buildRustCore) }

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2025.04.01")
    implementation(composeBom)
    implementation("androidx.core:core-ktx:1.16.0")
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation("com.google.mlkit:digital-ink-recognition:19.0.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("androidx.test:core:1.6.1")
    testImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}

import java.io.FileInputStream
import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

val stormSigningProps = Properties().apply {
    val secretsFile = file("F:/STORM ALL PROJECTS/.storm-secrets/storm_signing.properties")
    if (secretsFile.exists()) {
        FileInputStream(secretsFile).use { load(it) }
    }
}

android {
    namespace = "com.storm.retroarch"
    compileSdk = 36
    ndkVersion = "28.0.13004108"

    buildFeatures {
        buildConfig = true
    }

    defaultConfig {
        applicationId = "com.storm.retroarch"
        minSdk = 26
        // Official sideload RetroArch_aarch64 strategy: targetSdk = 28 places APK in untrusted_app_27 SELinux domain
        // on Android 10-16, permitting dlopen() on downloaded Libretro .so cores inside context.filesDir/cores/.
        //noinspection ExpiredTargetSdkVersion,OldTargetApi
        targetSdk = 28
        versionCode = 271
        versionName = "2.7.1"

        externalNativeBuild {
            cmake {
                cppFlags += listOf("-std=c++20", "-O3", "-fvisibility=hidden")
                arguments += listOf("-DANDROID_STL=c++_shared")
            }
        }
    }

    lint {
        checkReleaseBuilds = false
        abortOnError = false
        disable += setOf("ExpiredTargetSdkVersion", "OldTargetApi")
    }

    androidResources {
        noCompress += listOf("zip", "gz", "cht")
    }

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    signingConfigs {
        create("stormUnifiedRelease") {
            val certPath = stormSigningProps.getProperty("STORM_CERT_FILE") ?: System.getenv("STORM_CERT_FILE")
            if (!certPath.isNullOrBlank() && file(certPath).exists()) {
                storeFile = file(certPath)
                storePassword = stormSigningProps.getProperty("STORM_CERT_PASSWORD") ?: System.getenv("STORM_CERT_PASSWORD")
                keyAlias = stormSigningProps.getProperty("STORM_KEY_ALIAS") ?: System.getenv("STORM_KEY_ALIAS")
                keyPassword = stormSigningProps.getProperty("STORM_KEY_PASSWORD") ?: System.getenv("STORM_KEY_PASSWORD")
                enableV1Signing = true
                enableV2Signing = true
                enableV3Signing = true
            }
        }
    }

    flavorDimensions += "edition"
    productFlavors {
        create("modern") {
            dimension = "edition"
            minSdk = 26
            ndk {
                abiFilters += listOf("arm64-v8a", "x86_64")
            }
        }
        create("legacy") {
            dimension = "edition"
            minSdk = 26
            ndk {
                abiFilters += listOf("armeabi-v7a", "arm64-v8a", "x86", "x86_64")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            isShrinkResources = false
            signingConfig = signingConfigs.getByName("stormUnifiedRelease")
        }
        debug {
            signingConfig = signingConfigs.getByName("stormUnifiedRelease")
        }
    }

    packaging {
        jniLibs {
            // Extracts pre-bundled jniLibs/<abi>/lib*_android.so directly to nativeLibraryDir (apk_data_file SELinux)
            useLegacyPackaging = true
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation("androidx.activity:activity-ktx:1.8.1")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.documentfile:documentfile:1.0.1")
}

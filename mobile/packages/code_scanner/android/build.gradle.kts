group = "az.qonaqet.code_scanner"
version = "1.0.0"

buildscript {
    val kotlinVersion = "2.4.0"
    repositories {
        google()
        mavenCentral()
    }

    dependencies {
        classpath("com.android.tools.build:gradle:9.1.0")
        classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:$kotlinVersion")
    }
}

allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

plugins {
    id("com.android.library")
}

android {
    namespace = "az.qonaqet.code_scanner"

    compileSdk = 36

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    sourceSets {
        getByName("main") {
            java.srcDirs("src/main/kotlin")
        }
    }

    defaultConfig {
        minSdk = 24
    }

}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}


dependencies {
    // Google Code Scanner (Play services, ML Kit): öz UI-ı var, kamera icazəsi tələb etmir; modul Play services
    // tərəfindən yüklənir (CLAUDE.md → Mobile → QR scanning). Pub paketi deyil — Google-un rəsmi Maven artefaktı.
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")
}

package az.qonaqet.secure_store

import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel
import java.security.KeyStore
import java.util.concurrent.Executors
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Android Keystore üzərində açar-dəyər saxlama (E14-T05).
 *
 * Dəyərlər Keystore-da yaradılan, cihazdan çıxarıla bilməyən AES-256-GCM açarı ilə şifrələnir; yalnız şifrəli
 * mətn tətbiqin şəxsi SharedPreferences faylında qalır. Açar və fayl cihaz yenidən başlayandan sonra da qalır.
 * Ehtiyat nüsxədən bərpa olunmuş (açarı olmayan) dəyər oxunmursa silinir və `null` qaytarılır.
 * Dəyərlər heç vaxt loglanmır.
 */
class SecureStorePlugin : FlutterPlugin, MethodChannel.MethodCallHandler {
    private lateinit var channel: MethodChannel
    private lateinit var prefs: SharedPreferences
    private val io = Executors.newSingleThreadExecutor()

    override fun onAttachedToEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        prefs = binding.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        channel = MethodChannel(binding.binaryMessenger, CHANNEL)
        channel.setMethodCallHandler(this)
    }

    override fun onDetachedFromEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        channel.setMethodCallHandler(null)
        io.shutdown()
    }

    override fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        val key = call.argument<String>("key")
        if (key.isNullOrEmpty()) {
            result.error("invalid_argument", "key tələb olunur", null)
            return
        }
        // Keystore əməliyyatları UI thread-i bloklamasın.
        io.execute {
            try {
                val value: Any? = when (call.method) {
                    "read" -> read(key)
                    "write" -> {
                        write(key, call.argument<String>("value") ?: "")
                        null
                    }
                    "delete" -> {
                        prefs.edit().remove(key).commit()
                        null
                    }
                    else -> {
                        post { result.notImplemented() }
                        return@execute
                    }
                }
                post { result.success(value) }
            } catch (e: Exception) {
                post { result.error("secure_store_failed", e.javaClass.simpleName, null) }
            }
        }
    }

    private fun read(key: String): String? {
        val stored = prefs.getString(key, null) ?: return null
        return try {
            val bytes = Base64.decode(stored, Base64.NO_WRAP)
            val iv = bytes.copyOfRange(0, IV_BYTES)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(TAG_BITS, iv))
            String(cipher.doFinal(bytes, IV_BYTES, bytes.size - IV_BYTES), Charsets.UTF_8)
        } catch (e: Exception) {
            // Açar dəyişib/itib (məs. backup bərpası): oxunmayan dəyəri saxlamağın mənası yoxdur.
            prefs.edit().remove(key).commit()
            null
        }
    }

    private fun write(key: String, value: String) {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val encrypted = cipher.iv + cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        check(prefs.edit().putString(key, Base64.encodeToString(encrypted, Base64.NO_WRAP)).commit()) {
            "yazılmadı"
        }
    }

    private fun secretKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )
        return generator.generateKey()
    }

    private fun post(block: () -> Unit) {
        android.os.Handler(android.os.Looper.getMainLooper()).post(block)
    }

    private companion object {
        const val CHANNEL = "az.qonaqet/secure_store"
        const val PREFS = "az.qonaqet.secure_store"
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val KEY_ALIAS = "az.qonaqet.secure_store.aes"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_BYTES = 12
        const val TAG_BITS = 128
    }
}

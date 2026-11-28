package az.qonaqet.code_scanner

import android.app.Activity
import com.google.android.gms.common.api.CommonStatusCodes
import com.google.android.gms.common.api.ApiException
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.embedding.engine.plugins.activity.ActivityAware
import io.flutter.embedding.engine.plugins.activity.ActivityPluginBinding
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel

/**
 * QR skaneri: Google Code Scanner (Play services / ML Kit) — E14-T06.
 *
 * Skaner Play services-in öz ekranında işləyir və tətbiqə yalnız nəticəni qaytarır, ona görə tətbiq
 * kamera icazəsi istəmir. Play services yoxdursa və ya modul yüklənə bilmirsə `unavailable` qaytarılır;
 * Dart tərəfi onda əl ilə kod girişini təklif edir.
 */
class CodeScannerPlugin : FlutterPlugin, ActivityAware, MethodChannel.MethodCallHandler {
    private lateinit var channel: MethodChannel
    private var activity: Activity? = null

    override fun onAttachedToEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        channel = MethodChannel(binding.binaryMessenger, "az.qonaqet/code_scanner")
        channel.setMethodCallHandler(this)
    }

    override fun onDetachedFromEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        channel.setMethodCallHandler(null)
    }

    override fun onAttachedToActivity(binding: ActivityPluginBinding) {
        activity = binding.activity
    }

    override fun onDetachedFromActivityForConfigChanges() {
        activity = null
    }

    override fun onReattachedToActivityForConfigChanges(binding: ActivityPluginBinding) {
        activity = binding.activity
    }

    override fun onDetachedFromActivity() {
        activity = null
    }

    override fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        when (call.method) {
            "scan" -> scan(result)
            else -> result.notImplemented()
        }
    }

    private fun scan(result: MethodChannel.Result) {
        val current = activity
        if (current == null) {
            result.success(mapOf("status" to "unavailable", "reason" to "no_activity"))
            return
        }
        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
            .build()
        var answered = false
        fun answer(value: Map<String, String?>) {
            if (!answered) {
                answered = true
                result.success(value)
            }
        }
        GmsBarcodeScanning.getClient(current, options)
            .startScan()
            .addOnSuccessListener { barcode ->
                val value = barcode.rawValue
                answer(
                    if (value.isNullOrEmpty()) mapOf("status" to "cancelled")
                    else mapOf("status" to "success", "value" to value),
                )
            }
            .addOnCanceledListener { answer(mapOf("status" to "cancelled")) }
            .addOnFailureListener { e ->
                val cancelled = e is ApiException && e.statusCode == CommonStatusCodes.CANCELED
                answer(
                    if (cancelled) mapOf("status" to "cancelled")
                    else mapOf("status" to "unavailable", "reason" to e.javaClass.simpleName),
                )
            }
    }
}

package net.oxog.phonebox.deskpanel

import android.content.Context
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import java.util.Locale

/**
 * Reads the Translate card's source text aloud with the phone's own text-to-speech engine (the one picked in the
 * system's accessibility settings). Nothing leaves the phone. One utterance at a time: a new request cuts off the one
 * playing, and [stop] ends it when the panel pauses. [start] runs on the main thread; [speak] is called from the
 * WebView's bridge thread. When an utterance is over, the panel's main frame hears window.onAndroidTts('done').
 */
class Speaker(private val context: Context, private val js: (String) -> Unit) {
    private var engine: TextToSpeech? = null
    @Volatile private var ready = false
    @Volatile private var speaking = false

    /** Connects to the system's engine, so the first tap does not have to wait for it. Main thread. */
    fun start() {
        if (engine != null) return
        engine = try {
            TextToSpeech(context) { status -> ready = status == TextToSpeech.SUCCESS }.also { tts ->
                tts.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
                    override fun onStart(utteranceId: String?) {}
                    override fun onDone(utteranceId: String?) = ended()
                    // the engine's errors on every Android version end here: onError(String, int) calls it too
                    @Deprecated("Deprecated in Java")
                    override fun onError(utteranceId: String?) = ended()
                })
            }
        } catch (_: Exception) {
            null // a phone without any engine, or one that refuses to start
        }
    }

    /**
     * Reads [text] in the voice of [tag] (a BCP 47 tag). Returns "" when the reading started, "stopped" when a text
     * that was playing was stopped instead, or a reason for the panel: "no-tts" (no engine, or not ready yet) and
     * "no-voice" (the engine has no voice for the tag).
     */
    fun speak(text: String, tag: String): String {
        if (speaking) { stop(); return "stopped" }
        val tts = engine ?: return "no-tts"
        if (!ready) return "no-tts"
        when (tts.setLanguage(Locale.forLanguageTag(tag))) {
            TextToSpeech.LANG_MISSING_DATA, TextToSpeech.LANG_NOT_SUPPORTED -> return "no-voice"
        }
        speaking = true
        tts.speak(text.take(MAX_CHARS), TextToSpeech.QUEUE_FLUSH, null, UTTERANCE)
        return ""
    }

    /** Ends the utterance playing, if any. */
    fun stop() {
        if (!speaking) return
        speaking = false
        try { engine?.stop() } catch (_: Exception) { }
        done()
    }

    /** Releases the engine. The speaker cannot be used again afterwards. */
    fun shutdown() {
        speaking = false
        ready = false
        try { engine?.shutdown() } catch (_: Exception) { }
        engine = null
    }

    // The progress listener runs on a binder thread: the panel is told through the callback, which uses the main thread
    private fun ended() {
        speaking = false
        done()
    }

    private fun done() = js("window.onAndroidTts && window.onAndroidTts('done')")

    companion object {
        private const val UTTERANCE = "translate"
        private const val MAX_CHARS = 2000
    }
}

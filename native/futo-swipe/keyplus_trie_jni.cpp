// KeyPlus: a vocabulary trie for any alphabet, from a combined word list
// (AOSP's "word=…,f=…" lines), so a keyboard with no trie code of its own
// can hand SwipeDecoder.setMode the dictionaries it decodes against.
// Appended to jni/swipe_jni.cpp by KeyPlus's build of the library.

#include "swipe_decoder/trie.hpp"

extern "C" {

/** A trie over the letters given, filled from the word list; 0 where the list cannot be read. */
JNIEXPORT jlong JNICALL
Java_org_futo_ml_inference_SwipeDecoder_nativeLoadTrie(JNIEnv* env, jclass, jstring letters, jstring vocabPath, jstring freqKey) {
    auto* trie = new Trie(jstr_or_empty(env, letters));
    ParsedCombined combined;
    if (!parse_combined_vocab(jstr_or_empty(env, vocabPath), jstr_or_empty(env, freqKey), combined)) {
        delete trie;
        return 0;
    }

    apply_parsed_to_trie(combined, *trie);
    return reinterpret_cast<jlong>(trie);
}

/** The trie as the ITrie setMode takes; valid while the trie lives. */
JNIEXPORT jlong JNICALL
Java_org_futo_ml_inference_SwipeDecoder_nativeTrieHandle(JNIEnv*, jclass, jlong trie) {
    return reinterpret_cast<jlong>(reinterpret_cast<Trie*>(trie)->itrie());
}

JNIEXPORT void JNICALL
Java_org_futo_ml_inference_SwipeDecoder_nativeDestroyTrie(JNIEnv*, jclass, jlong trie) {
    delete reinterpret_cast<Trie*>(trie);
}

}

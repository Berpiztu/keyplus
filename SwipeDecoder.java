/*
 * Adapted from FUTO's swipe-library (android/src/main/kotlin/org/futo/ml/
 * inference/SwipeDecoder.kt), https://gitlab.futo.org/keyboard/swipe-library,
 * GPL-3.0: the class, its nested classes and their members carry the names
 * libswipe_jni.so (native/futo-swipe) binds to and builds.
 *
 * This program is free software: you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by the Free
 * Software Foundation, either version 3 of the License, or (at your option)
 * any later version.
 */
package org.futo.ml.inference;

/**
 * FUTO Swipe's engine for Berpiztu AI Keyboard: an encoder that reads a
 * swipe over any layout, decoding it against a word list's trie. Called from
 * C# (SwipeTyping.cs) through JNI, so it takes and returns Java's own types.
 */
public final class SwipeDecoder {
    static {
        System.loadLibrary("swipe_jni");
    }

    /** How many words the beam search keeps alive while it reads a swipe, FUTO's default. */
    private static final int BeamWidth = 100;

    private SwipeDecoder() {
    }

    /** A word the native library read, as it builds it. */
    public static final class Result {
        public final String word;
        public final float score;
        public final float ctcScore;
        public final float lmScore;

        public Result(String word, float score, float ctcScore, float lmScore) {
            this.word = word;
            this.score = score;
            this.ctcScore = ctcScore;
            this.lmScore = lmScore;
        }
    }

    /** A stretch of a swipe, as the native library reads it: x and y from 0 to 1 over the letters, t in milliseconds. */
    public static final class SwipeSeg {
        public final float[] x;
        public final float[] y;
        public final float[] t;

        public SwipeSeg(float[] x, float[] y, float[] t) {
            this.x = x;
            this.y = y;
            this.t = t;
        }
    }

    /** An engine over the encoder at that path, a metadata.json beside it: its handle, 0 where it failed. */
    public static long create(String encoderPath) {
        return nativeInit(encoderPath, null, 2, BeamWidth, 4, true, "f", null, null);
    }

    /** A trie over the letters given, from a combined word list ("word=…,f=…"): its handle, 0 where the list could not be read. */
    public static long loadDictionary(String letters, String vocabPath) {
        return nativeLoadTrie(letters, vocabPath, "f");
    }

    /** The letters' centres, from 0 to 1 over the letters, and the dictionary decoded against. */
    public static boolean setLayout(long engine, String letters, float[] cx, float[] cy, long dictionary) {
        return nativeSetMode(engine, letters, cx, cy, new long[] { nativeTrieHandle(dictionary) }, null, null, null);
    }

    /** The words a swipe reads as, the likeliest first. */
    public static String[] recognize(long engine, float[] x, float[] y, float[] t, int topK) {
        Result[] results = nativeRecognize(engine, new SwipeSeg[] { new SwipeSeg(x, y, t) }, new SwipeSeg[0], topK, null, BeamWidth, null);
        if (results == null) {
            return new String[0];
        }

        String[] words = new String[results.length];
        for (int i = 0; i < results.length; i++) {
            words[i] = results[i].word;
        }

        return words;
    }

    private static native long nativeInit(String encoderPath, String decoderPath, int threads, int beamWidth, int topK,
                                          boolean useExpansion, String freqKey, String lmModelPath, String lmVocabPath);

    private static native Result[] nativeRecognize(long handle, SwipeSeg[] leftSegs, SwipeSeg[] rightSegs, int topK,
                                                   String[] context, int beamWidth, float[] trieWeights);

    private static native boolean nativeSetMode(long handle, String letters, float[] cx, float[] cy, long[] tries,
                                                String decoderPath, String lmModelPath, String lmVocabPath);

    private static native long nativeLoadTrie(String letters, String vocabPath, String freqKey);

    private static native long nativeTrieHandle(long trie);
}

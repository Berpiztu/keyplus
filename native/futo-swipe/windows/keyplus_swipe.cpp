// KeyPlus: FUTO Swipe for Berpiztu AI Keyboard on Windows, as a C API in
// keyplus_swipe.dll, the calls the Android keyboard makes through JNI
// (SwipeDecoder.java, jni/swipe_jni.cpp, ../keyplus_trie_jni.cpp): the engine
// made with the encoder, a trie of a word list, the letters' centres and the
// trie handed to it, and a swipe read as words. Built by build.ps1.

#include "swipe_decoder/engine.hpp"
#include "swipe_decoder/layout.hpp"
#include "swipe_decoder/trie.hpp"

#include <cstring>
#include <string>
#include <vector>

using namespace swipe_decoder;

#define KEYPLUS_API extern "C" __declspec(dllexport)

/** The engine with the encoder at a path (metadata.json beside it); null where it cannot be loaded. */
KEYPLUS_API void* keyplus_swipe_create(const char* encoder_path, int threads, int beam_width, int top_k) {
    SwipeEngine::Config config;
    config.encoder_path = encoder_path ? encoder_path : "";
    config.num_threads = threads;
    config.beam_width = beam_width;
    config.top_k = top_k;
    auto* engine = new SwipeEngine();
    if (!engine->init(config)) {
        delete engine;
        return nullptr;
    }

    return engine;
}

KEYPLUS_API void keyplus_swipe_destroy(void* engine) {
    delete static_cast<SwipeEngine*>(engine);
}

/** A trie over the letters given, filled from a combined word list ("word=…,f=…" lines); null where the list cannot be read. */
KEYPLUS_API void* keyplus_swipe_load_trie(const char* letters, const char* vocab_path, const char* freq_key) {
    auto* trie = new Trie(letters ? letters : "");
    ParsedCombined combined;
    if (!parse_combined_vocab(vocab_path ? vocab_path : "", freq_key ? freq_key : "f", combined)) {
        delete trie;
        return nullptr;
    }

    apply_parsed_to_trie(combined, *trie);
    return trie;
}

KEYPLUS_API void keyplus_swipe_destroy_trie(void* trie) {
    delete static_cast<Trie*>(trie);
}

/** The letters (UTF-8, in Unicode's order), their centres from 0 to 1 over the letters' room, and the trie to read against. */
KEYPLUS_API int keyplus_swipe_set_layout(void* engine, const char* letters, const float* cx, const float* cy, int keys, void* trie) {
    if (!engine || !letters || !cx || !cy || !trie) {
        return 0;
    }

    SwipeEngine::Mode mode;
    mode.layout = make_layout(std::string(letters), cx, cy, keys);
    mode.dictionaries = std::vector<ITrie*>{static_cast<Trie*>(trie)->itrie()};
    return static_cast<SwipeEngine*>(engine)->setMode(mode) ? 1 : 0;
}

/**
 * A swipe read: its points (x and y from 0 to 1 over the letters, t in
 * milliseconds from the first) as at most top_k words, the likeliest first,
 * written UTF-8 into out one to a line; how many words, or -1 where out is
 * too small.
 */
KEYPLUS_API int keyplus_swipe_recognize(void* engine, const float* x, const float* y, const float* t, int points,
                                        int top_k, int beam_width, char* out, int out_size) {
    if (!engine || !x || !y || !t || points <= 0 || !out || out_size <= 0) {
        return 0;
    }

    auto words = static_cast<SwipeEngine*>(engine)->recognize(x, y, t, static_cast<size_t>(points), {}, top_k, beam_width);
    std::string joined;
    int count = 0;
    for (const auto& word : words) {
        if (count == top_k) {
            break;
        }

        joined += word.word;
        joined += '\n';
        ++count;
    }

    if (static_cast<int>(joined.size()) + 1 > out_size) {
        return -1;
    }

    std::memcpy(out, joined.c_str(), joined.size() + 1);
    return count;
}

#!/bin/bash
# FUTO swipe-library built for Android (arm64-v8a, x86_64) inside the
# futo-swipe-build container; the .so files end up in /out/<abi>/.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
step() { echo; echo "=== $(date '+%H:%M:%S') $*"; }

step "tools"
apt-get update -qq
apt-get install -y -qq git python3 python3-venv python3-pip build-essential curl unzip make ca-certificates >/dev/null

step "NDK"
for v in r27d r27c r27b; do
    if curl -fsI "https://dl.google.com/android/repository/android-ndk-$v-linux.zip" >/dev/null; then NDKV=$v; break; fi
done
echo "NDK $NDKV"
if [ ! -d "/opt/android-ndk-$NDKV" ]; then
    curl -fsSLo /tmp/ndk.zip "https://dl.google.com/android/repository/android-ndk-$NDKV-linux.zip"
    unzip -q /tmp/ndk.zip -d /opt
    rm /tmp/ndk.zip
fi
export ANDROID_NDK_HOME="/opt/android-ndk-$NDKV"

step "sources"
mkdir -p /work
cd /work
if [ ! -d swipe-library ]; then
    git clone --recursive https://gitlab.futo.org/keyboard/swipe-library.git
fi
cd swipe-library
git log -1 --format='swipe-library %h %cd'
git -C third_party/executorch log -1 --format='executorch %h %cd'

step "KeyPlus trie JNI"
if ! grep -q nativeLoadTrie jni/swipe_jni.cpp; then
    sed 's/\r$//' /out/keyplus_trie_jni.cpp >> jni/swipe_jni.cpp
fi
grep -c 'Java_org_futo_ml_inference_SwipeDecoder_native' jni/swipe_jni.cpp

step "build"
# The library's own configure step calls plain cmake: the venv's, which the
# Makefile installs, is the one recent enough.
export PATH="/work/swipe-library/.venv/bin:$PATH"
make android-arm64-v8a android-x86_64 JOBS=16

step "copy"
for abi in arm64-v8a x86_64; do
    mkdir -p "/out/$abi"
    cp "build-android-$abi/libswipe_jni.so" "/out/$abi/"
    triple=$([ "$abi" = arm64-v8a ] && echo aarch64-linux-android || echo x86_64-linux-android)
    cp "$ANDROID_NDK_HOME/toolchains/llvm/prebuilt/linux-x86_64/sysroot/usr/lib/$triple/libc++_shared.so" "/out/$abi/"
done
ls -l /out/*/

step "BUILD DONE"

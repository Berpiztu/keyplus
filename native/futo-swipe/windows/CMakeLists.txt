# keyplus_swipe.dll: FUTO Swipe (swipe-library) for Berpiztu AI Keyboard on
# Windows x64, with ExecuTorch built as a subproject with the options FUTO's
# Makefile gives it (XNNPACK, module, tensor, data loaders), and the C API of
# keyplus_swipe.cpp. Configured and built by build.ps1, with clang-cl, as
# ExecuTorch asks on Windows.

cmake_minimum_required(VERSION 3.24)
project(keyplus_swipe LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(SWIPE_LIBRARY "" CACHE PATH "swipe-library's checkout, its executorch submodule initialized")
if(NOT EXISTS "${SWIPE_LIBRARY}/third_party/executorch/CMakeLists.txt")
    message(FATAL_ERROR "SWIPE_LIBRARY must be swipe-library's checkout with its submodules")
endif()

set(EXECUTORCH_ROOT "${SWIPE_LIBRARY}/third_party/executorch")
set(EXECUTORCH_BUILD_XNNPACK ON CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_EXTENSION_MODULE ON CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_EXTENSION_TENSOR ON CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_EXTENSION_DATA_LOADER ON CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_EXTENSION_FLAT_TENSOR ON CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_EXTENSION_NAMED_DATA_MAP ON CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_KERNELS_QUANTIZED OFF CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(EXECUTORCH_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(XNNPACK_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(XNNPACK_BUILD_BENCHMARKS OFF CACHE BOOL "" FORCE)
set(XNNPACK_ENABLE_SPARSE OFF CACHE BOOL "" FORCE)
add_subdirectory("${EXECUTORCH_ROOT}" executorch)

# engine.cpp includes its default scoring as a raw string, generated/scoring.json,
# as swipe-library's own CMakeLists makes it (make_includable).
file(READ "${SWIPE_LIBRARY}/models/scoring.json" SCORING)
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/generated/scoring.json" "R\"for_c++_include(\n${SCORING})for_c++_include\"")

add_library(keyplus_swipe SHARED
    keyplus_swipe.cpp
    "${SWIPE_LIBRARY}/src/resampler.cpp"
    "${SWIPE_LIBRARY}/src/beam_search.cpp"
    "${SWIPE_LIBRARY}/src/trie.cpp"
    "${SWIPE_LIBRARY}/src/unicode_case.cpp"
    "${SWIPE_LIBRARY}/src/encoder.cpp"
    "${SWIPE_LIBRARY}/src/decoder.cpp"
    "${SWIPE_LIBRARY}/src/engine.cpp"
    "${SWIPE_LIBRARY}/src/context_lm.cpp"
    "${SWIPE_LIBRARY}/src/model_metadata.cpp"
)
target_include_directories(keyplus_swipe PRIVATE
    "${CMAKE_CURRENT_BINARY_DIR}"
    "${SWIPE_LIBRARY}/include"
    "${EXECUTORCH_ROOT}/.."
    "${EXECUTORCH_ROOT}/runtime/core/portable_type/c10"
    "${EXECUTORCH_ROOT}/third-party/json/include"
)
target_compile_definitions(keyplus_swipe PRIVATE HAVE_EXECUTORCH C10_USING_CUSTOM_GENERATED_MACROS NOMINMAX _USE_MATH_DEFINES)
target_link_libraries(keyplus_swipe PRIVATE
    executorch
    portable_ops_lib
    xnnpack_backend
    extension_module_static
    extension_tensor
    extension_data_loader
    extension_flat_tensor
    extension_named_data_map
)

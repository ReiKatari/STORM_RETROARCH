cmake_minimum_required(VERSION 3.22.1)
project(storm_retroarch_jni LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(storm_retroarch_jni SHARED
    storm_libretro_jni.cpp
)

target_include_directories(storm_retroarch_jni PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_compile_options(storm_retroarch_jni PRIVATE
    -O3
    -fvisibility=hidden
    -Wall
    -Wextra
    -Wno-unused-parameter
)

target_link_options(storm_retroarch_jni PRIVATE
    "-Wl,-z,max-page-size=16384"
)

find_library(ANDROID_LIB android REQUIRED)
find_library(JNIGRAPHICS_LIB jnigraphics REQUIRED)
find_library(LOG_LIB log REQUIRED)
find_library(DL_LIB dl REQUIRED)
find_library(GLESV3_LIB GLESv3 REQUIRED)
find_library(EGL_LIB EGL REQUIRED)

target_link_libraries(storm_retroarch_jni PRIVATE
    ${ANDROID_LIB}
    ${JNIGRAPHICS_LIB}
    ${LOG_LIB}
    ${DL_LIB}
    ${GLESV3_LIB}
    ${EGL_LIB}
)

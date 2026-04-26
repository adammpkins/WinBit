cmake_minimum_required(VERSION 3.21)

include(GenerateExportHeader)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(PRODUCT_VERSION_MAJOR 0)
set(PRODUCT_VERSION_MINOR 1)
set(PRODUCT_VERSION_PATCH 0)

if(${CMAKE_SYSTEM_NAME} MATCHES "Windows")
  set(CMAKE_SYSTEM_VERSION 10.0 CACHE TYPE INTERNAL FORCE)
endif()

project(lts
        LANGUAGES CXX
        VERSION ${PRODUCT_VERSION_MAJOR}.${PRODUCT_VERSION_MINOR}.${PRODUCT_VERSION_PATCH})

# Match the CRT linkage of the vcpkg *-windows-static triplets libtorrent is
# built against; otherwise our /MD objects clash with libtorrent's /MT archive
# at link time. Must come after project() — MSVC isn't defined before the
# compiler is detected.
if(MSVC)
        set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

find_package(LibtorrentRasterbar 2.0 CONFIG REQUIRED)
find_package(magic_enum CONFIG REQUIRED)

include_directories(${CMAKE_BINARY_DIR}/include include)

# rpath — ensures the built shared library finds sibling dependencies at load time
if(${CMAKE_SYSTEM_NAME} MATCHES "Darwin")
  set(CMAKE_BUILD_WITH_INSTALL_RPATH TRUE)
  set(CMAKE_INSTALL_RPATH "@loader_path")
elseif(${CMAKE_SYSTEM_NAME} MATCHES "Linux")
  set(CMAKE_BUILD_WITH_INSTALL_RPATH TRUE)
  set(CMAKE_INSTALL_RPATH "$ORIGIN")
endif()

add_library(lts SHARED
        src/library.cpp
        src/events.cpp
        src/settings.cpp)

set_target_properties(lts PROPERTIES
        SOVERSION ${PRODUCT_VERSION_MAJOR}
        VERSION ${PRODUCT_VERSION_MAJOR}.${PRODUCT_VERSION_MINOR}.${PRODUCT_VERSION_PATCH})

generate_export_header(lts EXPORT_FILE_NAME ${CMAKE_BINARY_DIR}/include/lts_export.h)

target_link_libraries(lts PUBLIC LibtorrentRasterbar::torrent-rasterbar)
target_link_libraries(lts PRIVATE magic_enum::magic_enum)

# Keep the C# runtimes directory in sync so dotnet build always picks up the
# latest native binary without a manual copy step.
if(CMAKE_SYSTEM_NAME MATCHES "Windows")
    add_custom_command(TARGET lts POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy
            $<TARGET_FILE:lts>
            "${CMAKE_CURRENT_SOURCE_DIR}/../LibtorrentSharp/runtimes/win-x64/native/lts.dll"
        COMMENT "lts → runtimes/win-x64/native/lts.dll"
    )
endif()

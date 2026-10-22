#!/usr/bin/env bash
# Builds HeavyProBridge.wasm with the MSFS 2024 SDK's own clang and wasm-ld,
# into the panel package's sources so fspackagetool ships it in modules/.
set -euo pipefail
# Git Bash would turn /c, /O2 and friends into paths.
export MSYS2_ARG_CONV_EXCL="*"
SDK="$(cygpath -m "${MSFS2024_SDK:-/c/MSFS 2024 SDK}")/WASM"
HERE="$(cygpath -m "$(cd "$(dirname "$0")" && pwd)")"
OUT="$HERE/../heavypro-panel/PackageSources/modules"
OBJ="$HERE/obj"
mkdir -p "$OUT" "$OBJ"

"$SDK/llvm/bin/clang-cl.exe" --target=wasm32-unknown-wasi /c /O2 /std:c++17 /EHs-c- /GR- /GS- \
  -Wno-ignored-attributes -Wno-unused-command-line-argument \
  /D__wasi__ /D_MSFS_WASM=1 /D_LIBCPP_HAS_NO_THREADS /D_WINDLL \
  -imsvc "$SDK/wasi-sysroot/include/c++/v1" -imsvc "$SDK/wasi-sysroot/include" -I "$SDK/include" \
  /Fo"$OBJ/HeavyProBridge.o" "$HERE/HeavyProBridge.cpp"

LIB="$SDK/wasi-sysroot/lib/wasm32-wasi"
"$SDK/llvm/bin/wasm-ld.exe" --no-entry --allow-undefined --export-dynamic --export-table --gc-sections -O3 \
  --export=__wasm_call_ctors --export=malloc --export=free \
  "$OBJ/HeavyProBridge.o" "$LIB/libc.a" "$LIB/libclang_rt.builtins-wasm32.a" \
  "$SDK/WasmVersions/MSFS_WasmVersions.a" \
  -o "$OUT/HeavyProBridge.wasm"
echo "built $OUT/HeavyProBridge.wasm"

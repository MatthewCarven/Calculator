#!/usr/bin/env bash
# Publish sizes for the throwaway probes. Restore is held to the local caches: an empty folder as
# the only source means a missing package fails here instead of downloading.
S="/c/Users/Admin/AppData/Local/Temp/claude/64370ccacde86111/2148620f-8ea3-424c-8a83-cb11a25a9795/scratchpad/ui"
EMPTY=$(cd "$S/empty-source" && pwd -W)
OUT="$S/out"
mkdir -p "$OUT"

one() {   # project label extra-args...
  proj=$1; label=$2; shift 2
  dest="$OUT/$proj-$label"
  rm -rf "$dest"
  cd "$S/$proj" || return
  start=$(date +%s)
  log=$(dotnet publish -c Release -r win-x64 -o "$(cygpath -w "$dest")" --nologo -v q -p:RestoreSources="$EMPTY" "$@" 2>&1)
  rc=$?
  took=$(( $(date +%s) - start ))
  if [ $rc -eq 0 ]; then
    bytes=$(du -sb "$dest" | cut -f1)
    files=$(find "$dest" -type f | wc -l)
    exe=$(find "$dest" -maxdepth 1 -name "*.exe" -printf "%s\n" | sort -n | tail -1)
    printf "%-5s %-26s rc=0  %8.1f MB  %4d files  exe %7.1f MB  (%ds)\n" "$proj" "$label" "$(python -c "print($bytes/1048576)")" "$files" "$(python -c "print(${exe:-0}/1048576)")" "$took"
  else
    msg=$(echo "$log" | grep -E "error" | sed 's/\[C:.*//; s/^.*error/error/' | sort -u | head -2 | cut -c1-200)
    printf "%-5s %-26s rc=%d  %s\n" "$proj" "$label" "$rc" "$msg"
  fi
  # rebuild from clean state next time
  rm -rf "$S/$proj/obj/Release/net10.0-windows/win-x64"
}

for p in wf wpf ava; do
  one $p fd --self-contained false
  one $p fd-single --self-contained false -p:PublishSingleFile=true
  one $p sc --self-contained true
  one $p sc-single --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
  one $p sc-single-zip --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
  one $p trim --self-contained true -p:PublishTrimmed=true
  one $p aot -p:PublishAot=true
done

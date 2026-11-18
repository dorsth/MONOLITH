#!/bin/sh
# Builds MONOLITH.dll: every mod under mods/ compiled together into one plugin file.
# The mods' own sources are used as they are; only their [assembly: ...] lines are left out (the bundle has its own).
# Also builds modmenu/ModMenu.dll, the stand-in for Mod Menu's API that ships next to it.
# Needs the .NET SDK and two paths:
#   LIB = <game>/STRAFTAT_Data/Managed   BEP = <game>/BepInEx/core
cd "$(dirname "$0")"
: "${LIB:?set LIB to <game>/STRAFTAT_Data/Managed}" "${BEP:?set BEP to <game>/BepInEx/core}"
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll /usr/share/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)
base=$(ls "$LIB"/*.dll "$BEP"/*.dll | grep -v Mono.Cecil | awk -F/ '!seen[$NF]++' | sed 's/^/-r:/')
rm -f modmenu/ModMenu.dll
dotnet "$CSC" -nologo -target:library -out:modmenu/ModMenu.dll -nostdlib -langversion:10 -optimize+ -debug- -deterministic -nowarn:CS0436,CS1701,CS1702 $base modmenu/src/*.cs 2>&1 | grep -E "error" | head -20
[ -f modmenu/ModMenu.dll ] || { echo "modmenu/ModMenu.dll not built"; exit 1; }
refs="$base -r:modmenu/ModMenu.dll"
# the menu's Russian: checked against the room each text has, then written out as Russian.cs
python3 lang/make.py || { echo "lang/make.py: a Russian text does not fit or is missing, not built"; exit 1; }
rm -rf obj && mkdir -p obj
contents=""
for d in mods/*/; do
  n=$(basename "$d")
  v=$(grep -rhoE 'AssemblyVersion\("[0-9]+\.[0-9]+\.[0-9]+' "$d" --include=*.cs | head -1 | sed 's/.*"//')
  contents="$contents$n $v, "
  find "$d" -name '*.cs' | while read f; do
    out="obj/$n/$(echo "${f#$d}" | tr '/' '_')"; mkdir -p "obj/$n"
    sed '/^\[assembly:/d' "$f" > "$out"
  done
done
contents=$(echo "$contents" | sed 's/, $//')
sed "s/@CONTENTS@/$contents/" Bundle.cs > obj/Bundle.cs
cp Menu.cs MenuPop.cs Layout.cs Profiles.cs Players.cs Previews.cs Keys.cs Lang.cs Russian.cs FrameCap.cs Stats.cs obj/
rm -f MONOLITH.dll
dotnet "$CSC" -nologo -target:library -out:MONOLITH.dll -define:DORSTH_BUNDLE -nostdlib -langversion:10 -unsafe -optimize+ -debug- -nowarn:CS0436,CS1701,CS1702 $refs $(find obj -name '*.cs') 2>&1 | grep -E "error|warning CS0(1|2)" | head -40
ls -la MONOLITH.dll 2>/dev/null && echo "holds: $contents"

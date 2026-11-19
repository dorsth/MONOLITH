#!/bin/sh
# builds MONOLITH.dll: every mod under mods/ compiled together into one plugin file.
# The mods' own sources are used as they are; only their [assembly: ...] lines are left out (the bundle has its own).
# LIB = <game>/STRAFTAT_Data/Managed, BEP = <game>/BepInEx/core, MODMENU = Mod Menu's dll
cd "$(dirname "$0")"
LIB=${LIB:-/home/claude/straftat/lib}; BEP=${BEP:-/home/claude/straftat/bep}; MODMENU=${MODMENU:-/home/claude/mods/plugins/ModMenu.dll}
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
# MORE = further folders with game dlls (every dll is taken once, the first found)
MORE=${MORE:-/home/claude/mods/refs/STRAFTAT/STRAFTAT_Data/Managed /mnt/user-data/uploads/STRAFTAT/STRAFTAT_Data/Managed}
refs=$(ls $LIB/*.dll $BEP/*.dll $MODMENU $(for m in $MORE; do ls $m/*.dll 2>/dev/null; done) | grep -v Mono.Cecil | awk -F/ '!seen[$NF]++' | sed 's/^/-r:/')
# the mods' own sources, taken fresh on every build (SYNC=0 builds from mods/ as it is)
if [ "${SYNC:-1}" = 1 ]; then
  for pair in "CameraTweaks /home/claude/mods/cam" "CombatSounds /home/claude/mods/combat" "ezCrosshair /home/claude/mods/ez/src" "ViewmodelTweaks /home/claude/mods/vm/src" "DorsthTweaks /home/claude/mods/dt/src" "TrueSens /home/claude/mods/truesens"; do
    set -- $pair
    [ -d "$2" ] && for f in mods/$1/*.cs; do [ -f "$2/$(basename $f)" ] && { chmod u+w "$f"; cp "$2/$(basename $f)" "$f"; }; done
  done
  # DorsthTweaks has Ultimate Tweaks in a folder of its own (since 1.3.0): taken whole
  rm -rf mods/DorsthTweaks/UltimateTweaks && cp -r /home/claude/mods/dt/src/UltimateTweaks mods/DorsthTweaks/
fi
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
cp Menu.cs MenuPop.cs Layout.cs Profiles.cs Players.cs Previews.cs Keys.cs Lang.cs Russian.cs FrameCap.cs Stats.cs GameConfig.cs obj/
rm -f MONOLITH.dll
dotnet $CSC -nologo -target:library -out:MONOLITH.dll -define:DORSTH_BUNDLE -nostdlib -langversion:10 -unsafe -optimize+ -debug- -nowarn:CS0436,CS1701,CS1702 $refs $(find obj -name '*.cs') 2>&1 | grep -E "error|warning CS0(1|2)" | cut -c1-260 | head -40
ls -la MONOLITH.dll 2>/dev/null && echo "holds: $contents"

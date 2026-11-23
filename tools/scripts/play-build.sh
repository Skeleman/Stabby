#!/usr/bin/env bash
# Self-contained playtest build in play/: exe in play/game, a snapshot of content/ and a baked map beside it.
. "$(dirname "$0")/env.sh"; cd "$ROOT"; set -e; mkdir -p out/unity
dotnet build -c Release -v q > out/unity/play-build.log 2>&1
rm -rf play; mkdir -p play/out/maps
dotnet run --project tools/Stabby.Cli -c Release --no-build -- bake --seed 3 --size 2048 --out play/out/maps/seed3-2048.stabbymap | tail -1
cp -r content play/content
"$UNITY_EXE" -batchmode -projectPath unity -executeMethod Stabby.Game.Editor.BuildGame.Windows -logFile out/unity/build.log > /dev/null 2>&1
grep "\[BuildGame\]" out/unity/build.log | tail -1
printf '@echo off\r\nset STABBY_CREATURE_PATH=instanced\r\nstart "" "%%~dp0game\Stabby.exe"\r\n' > play/Stabby-instanced-creatures.bat

#!/usr/bin/env bash
# Next free ids on origin/main, so lanes pick them before they collide.
. "$(dirname "$0")/env.sh"; cd "$ROOT"; git fetch -q origin
show() { git show "origin/main:$1"; }
gv=$(show src/Stabby.World/WorldConfig.cs | grep -o -E "GeneratorVersion = [0-9]+" | grep -o -E "[0-9]+")
ph=$(show unity/Assets/Game/Editor/GameCapture.cs | grep -o -E "phase (==|>=) *[0-9]+" | grep -o -E "[0-9]+$" | sort -n | tail -1)
obj=$(show src/Stabby.World/ObjectDefs.cs | grep -o -E "Shapes = [0-9]+" | grep -o -E "[0-9]+")
fe=$(show src/Stabby.World/Features.cs | grep -o -E "Shapes = [0-9]+" | grep -o -E "[0-9]+")
dk=$(show src/Stabby.World/Discoveries.cs | grep -m1 -o -E "[A-Za-z]+ = [0-9]+;" | grep -o -E "[0-9]+")
echo "origin/main $(git rev-parse --short origin/main)"
echo "GeneratorVersion next: $((gv+1))"
echo "capture phase next: $(( (ph/10+1)*10 )) (highest used $ph)"
echo "object shape next: $obj   feature edit shape next: $fe   discovery kind next: $((dk+1))"
echo "Tell the supervisor which you take; ids are settled again at merge."

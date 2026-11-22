# Sourced by the other scripts. Override UNITY_EXE or DOTNET_HOME in the environment.
UNITY_EXE="${UNITY_EXE:-/c/Unity/6000.5.8f1/Editor/Unity.exe}"
if [ -z "$DOTNET_HOME" ] && [ -n "$LOCALAPPDATA" ] && [ -x "$(cygpath -u "$LOCALAPPDATA")/Microsoft/dotnet/dotnet.exe" ]; then DOTNET_HOME="$(cygpath -u "$LOCALAPPDATA")/Microsoft/dotnet"; fi
if [ -n "$DOTNET_HOME" ]; then export PATH="$DOTNET_HOME:$PATH" DOTNET_ROOT="$DOTNET_HOME"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
ROOT="$(git rev-parse --show-toplevel)"

dotnet run $1 Runner/URCL.dll
BITS="$?"
if [ BITS != 1 ]; then
    case "$BITS" in
        8) I="I1" ;;
        16) I="I2" ;;
        32) I="I4" ;;
        64) I="I8" ;;
    esac
    cd Runner
    dotnet run -c Release -p:DefineConstants=$I -- URCL.dll -d
    cd ..
fi
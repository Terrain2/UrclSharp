dotnet run $1 Runner/URCL.dll
BITS="$?"
if [ BITS != 1 ]; then
    case "$BITS" in
        8) I="I1" ;;
        16) I="I2" ;;
        32) I="I4" ;;
        64) I="I8" ;;
    esac
    dotnet run --project Runner -c Release -p:DefineConstants=$I -- Runner/URCL.dll -d
fi
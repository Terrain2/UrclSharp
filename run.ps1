dotnet run $args[0] Runner\URCL.dll
$BITS = $LASTEXITCODE
if ($BITS -ne 1) {
    $I = Switch ($BITS) {
        8 { "I1" }
        16 { "I2" }
        32 { "I4" }
        64 { "I8" }
    }
    Set-Location Runner
    dotnet run -c Release -p:DefineConstants=$I -- URCL.dll -d
    Set-Location ..
}
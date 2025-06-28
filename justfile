[private]
just:
    just -l

# Clean ALL
[group('build')]
c:
    dotnet clean
    rm -r --force ./output
    cd PhantasmaPhoenix.Core && just c
    cd PhantasmaPhoenix.Cryptography && just c
    cd PhantasmaPhoenix.Protocol && just c
    cd PhantasmaPhoenix.VM && just c

# Build ALL
[group('build')]
b:
    dotnet build

# Build packages
[group('build')]
p:
    dotnet build -c Release
    dotnet pack -c Release
    dotnet publish PhantasmaPhoenix.Core/PhantasmaPhoenix.Core.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Core/PhantasmaPhoenix.Core.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.Cryptography/PhantasmaPhoenix.Cryptography.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Cryptography/PhantasmaPhoenix.Cryptography.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.Protocol/PhantasmaPhoenix.Protocol.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Protocol/PhantasmaPhoenix.Protocol.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.VM/PhantasmaPhoenix.VM.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.VM/PhantasmaPhoenix.VM.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0

[group('maintenance')]
eols:
    cd PhantasmaPhoenix.Core && just eols
    cd PhantasmaPhoenix.Cryptography && just eols
    cd PhantasmaPhoenix.Protocol && just eols
    cd PhantasmaPhoenix.VM && just eols

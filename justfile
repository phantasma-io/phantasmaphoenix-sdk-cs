set dotenv-load := true

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
    cd PhantasmaPhoenix.Cryptography.Legacy && just c
    cd PhantasmaPhoenix.InteropChains.Legacy && just c
    cd PhantasmaPhoenix.Protocol && just c
    cd PhantasmaPhoenix.RPC && just c
    cd PhantasmaPhoenix.VM && just c

# Build ALL
[group('build')]
b:
    dotnet build

# Build packages
[group('publish')]
p:
    dotnet build -c Release
    dotnet pack -c Release
    dotnet publish PhantasmaPhoenix.Core/PhantasmaPhoenix.Core.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Core/PhantasmaPhoenix.Core.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.Cryptography/PhantasmaPhoenix.Cryptography.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Cryptography/PhantasmaPhoenix.Cryptography.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.Cryptography.Legacy/PhantasmaPhoenix.Cryptography.Legacy.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Cryptography.Legacy/PhantasmaPhoenix.Cryptography.Legacy.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.InteropChains.Legacy/PhantasmaPhoenix.InteropChains.Legacy.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.InteropChains.Legacy/PhantasmaPhoenix.InteropChains.Legacy.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.Protocol/PhantasmaPhoenix.Protocol.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.Protocol/PhantasmaPhoenix.Protocol.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.RPC/PhantasmaPhoenix.RPC.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.RPC/PhantasmaPhoenix.RPC.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0
    dotnet publish PhantasmaPhoenix.VM/PhantasmaPhoenix.VM.csproj -c Release -f net6.0 -o ./output/dlls/net6.0
    dotnet publish PhantasmaPhoenix.VM/PhantasmaPhoenix.VM.csproj -c Release -f netstandard2.0 -o ./output/dlls/netstandard2.0

[group('publish')]
publish-nugets:
    sh ./scripts/publish-nugets.sh

[group('maintenance')]
eols:
    cd PhantasmaPhoenix.Core && just eols
    cd PhantasmaPhoenix.Cryptography && just eols
    cd PhantasmaPhoenix.Cryptography.Legacy && just eols
    cd PhantasmaPhoenix.InteropChains.Legacy && just eols
    cd PhantasmaPhoenix.Protocol && just eols
    cd PhantasmaPhoenix.RPC && just eols
    cd PhantasmaPhoenix.VM && just eols

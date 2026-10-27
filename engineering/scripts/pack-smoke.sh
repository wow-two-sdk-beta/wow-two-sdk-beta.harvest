#!/usr/bin/env bash
# Restores the packed nupkgs in a throwaway consumer, as a product would take them, and compiles a composition root
# that wires both forms behind the contract plus the test kit. A private packages folder keeps the local versions
# out of the machine's NuGet cache.
#
#   pack-smoke.sh <artifact-dir> <version>
set -euo pipefail

artifacts=$(cd "${1:?usage: pack-smoke.sh <artifact-dir> <version>}" && pwd)
version=${2:?pass the packed version}

consumer=$(mktemp -d)
trap 'rm -rf "$consumer"' EXIT

cat > "$consumer/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$artifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat > "$consumer/Consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WoW2.Sdk.Beta.Be.Harvest.Module" Version="$version" />
    <PackageReference Include="WoW2.Sdk.Beta.Be.Harvest.Client" Version="$version" />
    <PackageReference Include="WoW2.Sdk.Beta.Be.Harvest.Testing" Version="$version" />
  </ItemGroup>
</Project>
XML

cat > "$consumer/Program.cs" <<CS
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Runs;

var formOne = new ServiceCollection().AddLogging();
formOne.AddHarvest().Configure(options => options.ConnectionString = "Host=localhost;Database=harvest");
formOne.AddHarvestFetching();
formOne.AddHarvestReaders();
formOne.AddHarvestExtraction();
formOne.AddHarvestPosts();
formOne.AddHarvestDelivery();
formOne.AddHarvestEngine();

var formThree = new ServiceCollection();
formThree.AddHarvestClient(options => options.BaseAddress = new Uri("https://service.example.test/"));
formThree.AddHarvestDataClient();
formThree.AddHarvestRunClient();

foreach (var collection in new[] { formOne, formThree })
{
    using var provider = collection.BuildServiceProvider(validateScopes: true);
    using var scope = provider.CreateScope();
    scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    scope.ServiceProvider.GetRequiredService<IRunBroker>();
    scope.ServiceProvider.GetRequiredService<IPostBroker>();
    scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
}

Console.WriteLine(\$"pack smoke: {nameof(RegistrationBrokerConformance)}, {nameof(PostDeliveryBrokerConformance)} and {nameof(RunBrokerConformance)}; all brokers resolve in both packed forms");
CS

# No HTTP cache: a core version published minutes ago must resolve, not a cached index that predates it.
dotnet restore "$consumer/Consumer.csproj" --packages "$consumer/packages" --no-http-cache -v quiet
dotnet build "$consumer/Consumer.csproj" --no-restore -nologo -v quiet
dotnet "$consumer/bin/Debug/net10.0/Consumer.dll"

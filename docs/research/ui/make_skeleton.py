# A throwaway skeleton of the proposed solution, to check that the layout builds and that the worker
# lands beside the app. It uses the scratch copy of Natural, so nothing is built in the real folder.
from pathlib import Path

root = Path(__file__).parent / "skeleton"


def w(rel, text):
    p = root / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8", newline="\n")


w("Directory.Build.props", """<Project>
  <PropertyGroup>
    <NaturalRoot Condition="'$(NaturalRoot)' == ''">$(MSBuildThisFileDirectory)..\\natural-copy\\</NaturalRoot>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
""")

w("Calc.slnx", """<Solution>
  <Project Path="src/Calc.Protocol/Calc.Protocol.csproj" />
  <Project Path="src/Calc.Engine/Calc.Engine.csproj" />
  <Project Path="src/Calc.Worker/Calc.Worker.csproj" />
  <Project Path="src/Calc.ViewModels/Calc.ViewModels.csproj" />
  <Project Path="src/Calc.App/Calc.App.csproj" />
</Solution>
""")

w("src/Calc.Protocol/Calc.Protocol.csproj", """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
""")
w("src/Calc.Protocol/Wire.cs", """namespace Calc.Protocol;
public static class Wire { public const string Version = "1"; }
""")

w("src/Calc.Engine/Calc.Engine.csproj", """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\Calc.Protocol\\Calc.Protocol.csproj" />
    <ProjectReference Include="$(NaturalRoot)Natural.csproj" />
  </ItemGroup>
</Project>
""")
w("src/Calc.Engine/Evaluator.cs", """using Natural;
namespace Calc.Engine;
public static class Evaluator { public static string Sum(string a, string b) => (ApFloat.Parse(a) + ApFloat.Parse(b)).ToString("R"); }
""")

w("src/Calc.Worker/Calc.Worker.csproj", """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>calc-worker</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\Calc.Engine\\Calc.Engine.csproj" />
  </ItemGroup>
</Project>
""")
w("src/Calc.Worker/Program.cs", """Console.WriteLine(Calc.Engine.Evaluator.Sum(args[0], args[1]));
""")

w("src/Calc.ViewModels/Calc.ViewModels.csproj", """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\Calc.Protocol\\Calc.Protocol.csproj" />
  </ItemGroup>
</Project>
""")
w("src/Calc.ViewModels/Shell.cs", """namespace Calc.ViewModels;
public sealed class Shell { public string Version => Calc.Protocol.Wire.Version; }
""")

w("src/Calc.App/Calc.App.csproj", """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\Calc.ViewModels\\Calc.ViewModels.csproj" />
    <!-- Built first and copied beside the app, but not referenced: the app cannot see Natural. -->
    <ProjectReference Include="..\\Calc.Worker\\Calc.Worker.csproj" ReferenceOutputAssembly="false" OutputItemType="WorkerOutput" />
  </ItemGroup>
  <Target Name="CopyWorker" AfterTargets="Build">
    <ItemGroup>
      <WorkerFiles Include="..\\Calc.Worker\\bin\\$(Configuration)\\net10.0\\*" />
    </ItemGroup>
    <Copy SourceFiles="@(WorkerFiles)" DestinationFolder="$(OutDir)" SkipUnchangedFiles="true" />
  </Target>
</Project>
""")
w("src/Calc.App/Program.cs", """using System.Windows;
public static class Program
{
    [STAThread] public static void Main()
    {
        System.Console.WriteLine(new Calc.ViewModels.Shell().Version);
        // The next line must not compile: the app has no reference to Natural.
        // _ = typeof(Natural.ApFloat);
    }
}
""")
print("written")

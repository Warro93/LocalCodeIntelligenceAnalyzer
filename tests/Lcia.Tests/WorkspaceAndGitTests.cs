using Lcia.Application;
using Lcia.Domain;
using Lcia.Infrastructure;
using Lcia.Scanner;

namespace Lcia.Tests;

public sealed class WorkspaceDiscoveryTests
{
    private const string SlnContent = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Lib", "src\Lib\Lib.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Ext", "..\External\Ext.csproj", "{33333333-3333-3333-3333-333333333333}"
        EndProject
        Global
        EndGlobal
        """;

    private const string SlnxContent = """
        <Solution>
          <Folder Name="/src/">
            <Project Path="src/Lib/Lib.csproj" />
            <Project Path="src/Core/Core.csproj" />
          </Folder>
        </Solution>
        """;

    private const string LibProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace>Demo.Lib</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            <PackageReference Include="Serilog" />
            <ProjectReference Include="..\Core\Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string CoreProject = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
            <OutputType>Library</OutputType>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="System.Data, Version=4.0.0.0, Culture=neutral" />
          </ItemGroup>
        </Project>
        """;

    private const string CentralPackages = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="Serilog" Version="4.0.0" />
          </ItemGroup>
        </Project>
        """;

    private const string PackagesConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          <package id="Dapper" version="2.1.35" targetFramework="net48" />
        </packages>
        """;

    [Fact]
    public void ReadsSolutionsProjectsPackagesAndReferences()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.sln", SlnContent);
        temp.WriteFile("All.slnx", SlnxContent);
        temp.WriteFile("Directory.Packages.props", CentralPackages);
        temp.WriteFile("src/Lib/Lib.csproj", LibProject);
        temp.WriteFile("src/Core/Core.csproj", CoreProject);
        temp.WriteFile("src/Core/packages.config", PackagesConfig);

        var files = new FileSystemRepositoryScanner().Discover(temp.Path, new ScanOptions(), CancellationToken.None).Files;
        var warnings = new List<ScanWarning>();
        var projects = new WorkspaceDiscovery().Discover(temp.Path, files, warnings, CancellationToken.None);

        var sln = Assert.Single(projects, p => p.RelativePath == "App.sln");
        Assert.Equal(ProjectKind.Solution, sln.Kind);
        Assert.Equal(2, sln.References.Count);
        Assert.Contains(sln.References, r => r.Name == "Lib" && r.ExistsInRepository && r.TargetPath == "src/Lib/Lib.csproj");
        Assert.Contains(sln.References, r => r.Name == "Ext" && !r.ExistsInRepository);
        Assert.Contains(warnings, w => w.RelativePath == "App.sln" && w.Message.Contains("esterno", StringComparison.Ordinal));

        var slnx = Assert.Single(projects, p => p.RelativePath == "All.slnx");
        Assert.Equal(2, slnx.References.Count(r => r.ExistsInRepository));

        var lib = Assert.Single(projects, p => p.Name == "Lib");
        Assert.Equal("Microsoft.NET.Sdk", lib.Sdk);
        Assert.Equal("net10.0", lib.TargetFrameworks);
        Assert.Equal("Demo.Lib", lib.RootNamespace);
        Assert.Contains(lib.References, r => r.Kind == ProjectReferenceKind.Package && r.Name == "Newtonsoft.Json" && r.Version == "13.0.3");
        Assert.Contains(lib.References, r => r.Kind == ProjectReferenceKind.Package && r.Name == "Serilog" && r.Version!.StartsWith("4.0.0", StringComparison.Ordinal));
        Assert.Contains(lib.References, r => r.Kind == ProjectReferenceKind.Project && r.Name == "Core" && r.ExistsInRepository);

        var core = Assert.Single(projects, p => p.Name == "Core");
        Assert.Equal("v4.8", core.TargetFrameworks);
        Assert.Contains(core.References, r => r.Kind == ProjectReferenceKind.Assembly && r.Name == "System.Data" && r.Version == "4.0.0.0");
        Assert.Contains(core.References, r => r.Kind == ProjectReferenceKind.Package && r.Name == "Dapper" && r.Version!.StartsWith("2.1.35", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsUnreadableProjectFilesWithoutFailing()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Broken/Broken.csproj", "<Project><PropertyGroup>");
        var files = new FileSystemRepositoryScanner().Discover(temp.Path, new ScanOptions(), CancellationToken.None).Files;
        var warnings = new List<ScanWarning>();
        var project = Assert.Single(new WorkspaceDiscovery().Discover(temp.Path, files, warnings, CancellationToken.None));
        Assert.NotNull(project.ParseError);
        Assert.NotEmpty(warnings);
    }
}

public sealed class GitHeadReaderTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ReadsBranchAndCommitFromLooseReference()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".git/HEAD", "ref: refs/heads/feature/login\n");
        temp.WriteFile(".git/refs/heads/feature/login", Hash + "\n");
        var head = new GitHeadReader().TryRead(temp.Path);
        Assert.NotNull(head);
        Assert.Equal("feature/login", head.Branch);
        Assert.Equal(Hash, head.Commit);
        Assert.False(head.IsDetached);
    }

    [Fact]
    public void ReadsCommitFromPackedRefs()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".git/HEAD", "ref: refs/heads/main\n");
        temp.WriteFile(".git/packed-refs", $"# pack-refs with: peeled fully-peeled sorted\n{Hash} refs/heads/main\n");
        Assert.Equal(Hash, new GitHeadReader().TryRead(temp.Path)?.Commit);
    }

    [Fact]
    public void ReadsDetachedHeadAndWorktreeFile()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("real-git/HEAD", Hash + "\n");
        temp.WriteFile("work/.git", "gitdir: ../real-git\n");
        var head = new GitHeadReader().TryRead(temp.Combine("work"));
        Assert.NotNull(head);
        Assert.True(head.IsDetached);
        Assert.Null(head.Branch);
        Assert.Equal(Hash, head.Commit);
    }

    [Fact]
    public void ReturnsNullWithoutGit()
    {
        using var temp = new TempDirectory();
        Assert.Null(new GitHeadReader().TryRead(temp.Path));
    }
}

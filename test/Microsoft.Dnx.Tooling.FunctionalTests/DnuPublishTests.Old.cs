// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.AspNet.Testing.xunit;
using Microsoft.Dnx.CommonTestUtils;
using Microsoft.Dnx.Runtime;
using Microsoft.Extensions.PlatformAbstractions;
using Xunit;

namespace Microsoft.Dnx.Tooling.FunctionalTests.Old
{
    [Collection(nameof(PackageManagerFunctionalTestCollection))]
    public class DnuPublishTests
    {
        private readonly FrameworkName DnxCore50 = NuGet.VersionUtility.ParseFrameworkName("dnxcore50");
        private readonly FrameworkName Dnx451 = NuGet.VersionUtility.ParseFrameworkName("dnx451");
        private readonly FrameworkName Dnx46 = NuGet.VersionUtility.ParseFrameworkName("dnx46");
        private readonly string _projectName = "TestProject";
        private readonly string _outputDirName = "PublishOutput";
        private readonly PackageManagerFunctionalTestFixture _fixture;

        // null is an option which represents the situation when configuration option is omit from command line
        private static readonly string[] ConfigurationOptions = new string[] { "Debug", "Release", null };

        private static readonly string BatchFileTemplate = @"
@echo off
SET DNX_FOLDER={0}
SET ""LOCAL_DNX=%~dp0runtimes\%DNX_FOLDER%\bin\{1}.exe""

IF EXIST %LOCAL_DNX% (
  SET ""DNX_PATH=%LOCAL_DNX%""
)

for %%a in (%DNX_HOME%) do (
    IF EXIST %%a\runtimes\%DNX_FOLDER%\bin\{1}.exe (
        SET ""HOME_DNX=%%a\runtimes\%DNX_FOLDER%\bin\{1}.exe""
        goto :continue
    )
)

:continue

IF ""%HOME_DNX%"" NEQ """" (
  SET ""DNX_PATH=%HOME_DNX%""
)

IF ""%DNX_PATH%"" == """" (
  SET ""DNX_PATH={1}.exe""
)

@""%DNX_PATH%"" --project ""%~dp0src\{2}"" --configuration {3} {4} %*
";

        private static readonly string BashScriptTemplate = @"#!/usr/bin/env bash

SOURCE=""${{BASH_SOURCE[0]}}""
while [ -h ""$SOURCE"" ]; do # resolve $SOURCE until the file is no longer a symlink
  DIR=""$( cd -P ""$( dirname ""$SOURCE"" )"" && pwd )""
  SOURCE=""$(readlink ""$SOURCE"")""
  [[ $SOURCE != /* ]] && SOURCE=""$DIR/$SOURCE"" # if $SOURCE was a relative symlink, we need to resolve it relative to the path where the symlink file was located
done
DIR=""$( cd -P ""$( dirname ""$SOURCE"" )"" && pwd )""

exec ""{1}{2}"" --project ""$DIR/src/{0}"" --configuration {3} {4} ""$@""".Replace("\r\n", "\n");

        public DnuPublishTests(PackageManagerFunctionalTestFixture fixture)
        {
            _fixture = fixture;
        }

        public static IEnumerable<object[]> RuntimeComponents
        {
            get
            {
                return CommonTestUtils.TestUtils.GetRuntimeComponentsCombinations();
            }
        }

        public static IEnumerable<object[]> RuntimeComponentsWithConfigurationOptions
        {
            get
            {
                foreach (var combination in RuntimeComponents)
                {
                    foreach (var configuration in ConfigurationOptions)
                    {
                        yield return combination.Concat(new object[] { configuration }).ToArray();
                    }
                }
            }
        }

        public static IEnumerable<object[]> ClrRuntimeComponents
        {
            get
            {
                return CommonTestUtils.TestUtils.GetClrRuntimeComponents();
            }
        }

        public static IEnumerable<object[]> CoreClrRuntimeComponents
        {
            get
            {
                return CommonTestUtils.TestUtils.GetCoreClrRuntimeComponents();
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWebApp_RootAsPublicFolder(string flavor, string os, string architecture)
        {

            var projectStructure = @"{
  '.': ['project.json', 'Config.json', 'Program.cs', 'build_config1.bconfig', 'hosting.json'],
  'Views': {
    'Home': ['index.cshtml'],
    'Shared': ['_Layout.cshtml']
  },
  'Controllers': ['HomeController.cs'],
  'Models': ['User.cs', 'build_config2.bconfig'],
  'Build': ['build_config3.bconfig'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'wwwroot': {
    '.': ['project.json', 'Config.json', 'Program.cs', 'build_config1.bconfig', 'web.config'],
      'Views': {
        'Home': ['index.cshtml'],
        'Shared': ['_Layout.cshtml']
    },
    'Controllers': ['HomeController.cs'],
    'Models': ['User.cs', 'build_config2.bconfig'],
    'Build': ['build_config3.bconfig']
  },
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'Config.json', 'Program.cs', 'hosting.json'],
          'Views': {
            'Home': ['index.cshtml'],
            'Shared': ['_Layout.cshtml']
        },
        'Controllers': ['HomeController.cs'],
        'Models': ['User.cs']
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            var outputWebConfigTemplate = @"<configuration>
  <system.webServer>
    <handlers>
      <add name=""httpplatformhandler"" path=""*"" verb=""*"" modules=""httpPlatformHandler"" resourceType=""Unspecified"" />
    </handlers>
    <httpPlatform processPath=""..\approot\web.cmd"" arguments="""" stdoutLogEnabled=""true"" stdoutLogFile=""..\logs\stdout.log""></httpPlatform>
  </system.webServer>
</configuration>";

            string runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""publishExclude"": ""**.bconfig"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents("hosting.json", @"{
    ""WebRoot"": ""to_be_overridden""
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0} --wwwroot . --wwwroot-out wwwroot",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""publishExclude"": ""**.bconfig"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "hosting.json"), @"{
    ""WebRoot"": ""../../../wwwroot""
}")
                    .WithFileContents(Path.Combine("wwwroot", "project.json"), @"{
  ""publishExclude"": ""**.bconfig"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("wwwroot", "hosting.json"), @"{
    ""WebRoot"": ""to_be_overridden""
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}")
                    .WithFileContents(Path.Combine("wwwroot", "web.config"), outputWebConfigTemplate, testEnv.ProjectName);
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true, ignoreWhitespace: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWebApp_SubfolderAsPublicFolder(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'hosting.json', 'Config.json', 'Program.cs'],
  'public': {
    'Scripts': ['bootstrap.js', 'jquery.js'],
    'Images': ['logo.png'],
    'UselessFolder': ['file.useless']
  },
  'Views': {
    'Home': ['index.cshtml'],
    'Shared': ['_Layout.cshtml']
  },
  'Controllers': ['HomeController.cs'],
  'UselessFolder': ['file.useless'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'wwwroot': {
    'web.config': '',
    'Scripts': ['bootstrap.js', 'jquery.js'],
    'Images': ['logo.png'],
    'UselessFolder': ['file.useless']
  },
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'hosting.json', 'Config.json', 'Program.cs'],
          'Views': {
            'Home': ['index.cshtml'],
            'Shared': ['_Layout.cshtml']
        },
        'Controllers': ['HomeController.cs'],
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);
            var outputWebConfigTemplate = @"<configuration>
  <system.webServer>
    <handlers>
      <add name=""httpplatformhandler"" path=""*"" verb=""*"" modules=""httpPlatformHandler"" resourceType=""Unspecified"" />
    </handlers>
    <httpPlatform processPath=""..\approot\web.cmd"" arguments="""" stdoutLogEnabled=""true"" stdoutLogFile=""..\logs\stdout.log""></httpPlatform>
  </system.webServer>
</configuration>";

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""publishExclude"": ""**.useless"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents("hosting.json", @"{
    ""WebRoot"": ""public""
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0} --wwwroot-out wwwroot",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""publishExclude"": ""**.useless"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "hosting.json"), @"{
    ""WebRoot"": ""../../../wwwroot""
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}")
                    .WithFileContents(Path.Combine("wwwroot", "web.config"), outputWebConfigTemplate, testEnv.ProjectName);
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true, ignoreWhitespace: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWebApp_NonexistentFolderAsPublicFolder(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'hosting.json'],
}";
            var expectedOutputStructure = @"{
  'wwwroot': {
    'web.config': ''
  },
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'hosting.json'],
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents("hosting.json", @"{
    ""WebRoot"": ""wwwroot""
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: $"--out {testEnv.PublishOutputDirPath}",
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure);

                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: false));
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishConsoleApp(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'Config.json', 'Program.cs'],
  'Data': {
    'Input': ['data1.dat', 'data2.dat'],
    'Backup': ['backup1.dat', 'backup2.dat']
  },
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'Config.json', 'Program.cs'],
          'Data': {
            'Input': ['data1.dat', 'data2.dat']
          }
        }
      }
    }
  }".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""publishExclude"": ""Data/Backup/**"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""publishExclude"": ""Data/Backup/**"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}");
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishConsoleAppWithNoSourceOptionNormalizesVersionNumberWithRevisionNumberOfZero(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'Config.json', 'Program.cs'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'packages': {
      'PROJECT_NAME': {
        '1.0.0': {
          '.': ['PROJECT_NAME.1.0.0.nupkg', 'PROJECT_NAME.1.0.0.nupkg.sha512', 'PROJECT_NAME.nuspec'],
          'root': ['Config.json', 'project.json', 'project.lock.json'],
          'lib': {
            'dnx451': ['PROJECT_NAME.dll', 'PROJECT_NAME.xml']
          }
        }
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""version"": ""1.0.0.0"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "restore",
                    arguments: string.Empty,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: $"--no-source --out {testEnv.PublishOutputDirPath}",
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure);
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: false));
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishConsoleAppWithNoSourceOptionNormalizesVersionNumberWithNoBuildNumber(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'Config.json', 'Program.cs'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'packages': {
      'PROJECT_NAME': {
        '1.0.0-beta': {
          '.': ['PROJECT_NAME.1.0.0-beta.nupkg', 'PROJECT_NAME.1.0.0-beta.nupkg.sha512', 'PROJECT_NAME.nuspec'],
          'root': ['Config.json', 'project.json', 'project.lock.json'],
          'lib': {
            'dnx451': ['PROJECT_NAME.dll', 'PROJECT_NAME.xml']
          }
        }
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""version"": ""1.0-beta"",
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "restore",
                    arguments: string.Empty,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: $"--no-source --out {testEnv.PublishOutputDirPath}",
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure);
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: false));
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishCopiesAllProjectsToOneSrcFolder(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);
            var solutionStructure = @"{
  'global.json': '',
  'source1': {
    'ProjectA': ['project.json', 'Program.cs']
  },
  'source2': {
    'ProjectB': ['project.json', 'Program.cs']
  },
  'source3': {
    'ProjectC': ['project.json', 'Program.cs']
  }
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'ProjectA': ['project.json', 'project.lock.json', 'Program.cs'],
      'ProjectB': ['project.json', 'project.lock.json', 'Program.cs'],
      'ProjectC': ['project.json', 'project.lock.json', 'Program.cs']
      }
    }
  }";

            using (var tempDir = new DisposableDir())
            {
                var solutionDir = Path.Combine(tempDir, "solution");
                var outputDir = Path.Combine(tempDir, "output");

                DirTree.CreateFromJson(solutionStructure)
                    .WithFileContents("global.json", @"{
  ""projects"": [""source1"", ""source2"", ""source3""]
}")
                    .WithFileContents(Path.Combine("source1", "ProjectA", "project.json"), @"{
  ""version"": ""1.0.0"",
  ""dependencies"": {
    ""ProjectB"": ""1.0.0""
  },
  ""frameworks"": {
    ""dnx451"": { }
  }
}")
                    .WithFileContents(Path.Combine("source2", "ProjectB", "project.json"), @"{
  ""version"": ""1.0.0"",
  ""dependencies"": {
    ""ProjectC"": ""1.0.0""
  },
  ""frameworks"": {
    ""dnx451"": { }
  }
}")
                    .WithFileContents(Path.Combine("source3", "ProjectC", "project.json"), @"{
  ""version"": ""1.0.0"",
  ""frameworks"": {
    ""dnx451"": { }
  }
}")
                    .WriteTo(solutionDir);

                DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "restore",
                    arguments: $"",
                    workingDir: solutionDir);

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: $"{Path.Combine(solutionDir, "source1", "ProjectA")} --out {outputDir}");
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure);

                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(outputDir, compareFileContents: false));
                Assert.Equal(@"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}", File.ReadAllText(Path.Combine(outputDir, "approot", "global.json")));
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)] // Mono DNX still only supports one framework (dnx451) at the moment.
        [MemberData(nameof(ClrRuntimeComponents))]
        public void PublishMultipleProjectsWithDifferentTargetFrameworks(string flavor, string os, string architecture)
        {
            string projectStructure = @"{
    'App': [ 'project.json' ],
    'Lib': [ 'project.json' ],
    '.': [ 'global.json' ]
}";

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("global.json", @"{ ""projects"": [ ""."" ] }")
                    .WithFileContents("App/project.json", @"{
  ""dependencies"": { ""Lib"": """" },
  ""frameworks"": {
    ""dnx46"": {}
  }
}")
                    .WithFileContents("Lib/project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "restore",
                    arguments: "",
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: Path.Combine(testEnv.ProjectPath, "App"));
                Assert.Equal(0, exitCode);

                // App lock file has DNX 4.6 target referring to lib with DNX 4.5.1 target
                var appLockFile = new LockFileReader().Read(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", "App", "project.lock.json"));
                foreach(var target in appLockFile.Targets)
                {
                    // Rid will differ
                    Assert.Equal(Dnx46, target.TargetFramework);
                    var lib = target.Libraries.FirstOrDefault(l => l.Name.Equals("Lib"));
                    Assert.NotNull(lib);
                    Assert.Equal("project", lib.Type);
                    Assert.Equal(Dnx451, lib.TargetFramework);
                }

                // Lib lock file has DNX 4.5.1 target
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", "Lib", "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void FoldersAsFilePatternsAutoGlob(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'FileWithoutExtension'],
  'UselessFolder1': {
    '.': ['file1.txt', 'file2.css', 'file_without_extension'],
    'SubFolder': ['file3.js', 'file4.html', 'file_without_extension']
  },
  'UselessFolder2': {
    '.': ['file1.txt', 'file2.css', 'file_without_extension'],
    'SubFolder': ['file3.js', 'file4.html', 'file_without_extension']
  },
  'UselessFolder3': {
    '.': ['file1.txt', 'file2.css', 'file_without_extension'],
    'SubFolder': ['file3.js', 'file4.html', 'file_without_extension']
  },
  'MixFolder': {
    'UsefulSub': ['useful.txt', 'useful.css', 'file_without_extension'],
    'UselessSub1': ['file1.js', 'file2.html', 'file_without_extension'],
    'UselessSub2': ['file1.js', 'file2.html', 'file_without_extension'],
    'UselessSub3': ['file1.js', 'file2.html', 'file_without_extension'],
    'UselessSub4': ['file1.js', 'file2.html', 'file_without_extension'],
    'UselessSub5': ['file1.js', 'file2.html', 'file_without_extension']
  },
  '.git': ['index', 'HEAD', 'log'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json'],
        'MixFolder': {
          'UsefulSub': ['useful.txt', 'useful.css', 'file_without_extension']
        }
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                // REVIEW: Paths with \ don't work on *nix so we put both in here for now
                // We need a good strategy to test \\ and / on windows and / on *nix and osx
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  },
  ""publishExclude"": [
    ""FileWithoutExtension"",
    ""UselessFolder1"",
    ""UselessFolder2/"",
    ""UselessFolder3\\"",
    ""UselessFolder3/"",
    ""MixFolder/UselessSub1/"",
    ""MixFolder\\UselessSub2\\"",
    ""MixFolder/UselessSub2/"",
    ""MixFolder/UselessSub3\\"",
    ""MixFolder/UselessSub3/"",
    ""MixFolder/UselessSub4"",
    ""MixFolder\\UselessSub5"",
    ""MixFolder/UselessSub5"",
    "".git""
  ]
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""frameworks"": {
    ""dnx451"": {}
  },
  ""publishExclude"": [
    ""FileWithoutExtension"",
    ""UselessFolder1"",
    ""UselessFolder2/"",
    ""UselessFolder3\\"",
    ""UselessFolder3/"",
    ""MixFolder/UselessSub1/"",
    ""MixFolder\\UselessSub2\\"",
    ""MixFolder/UselessSub2/"",
    ""MixFolder/UselessSub3\\"",
    ""MixFolder/UselessSub3/"",
    ""MixFolder/UselessSub4"",
    ""MixFolder\\UselessSub5"",
    ""MixFolder/UselessSub5"",
    "".git""
  ]
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}");
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void WildcardMatchingFacts(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json'],
  'UselessFolder1': {
    '.': ['uselessfile1.txt', 'uselessfile2'],
    'SubFolder': ['uselessfile3.js', 'uselessfile4']
  },
  'UselessFolder2': {
    '.': ['uselessfile1.txt', 'uselessfile2'],
    'SubFolder': ['uselessfile3.js', 'uselessfile4']
  },
  'UselessFolder3': {
    '.': ['uselessfile1.txt', 'uselessfile2'],
    'SubFolder': ['uselessfile3.js', 'uselessfile4']
  },
  'MixFolder1': {
    '.': ['uselessfile1.txt', 'uselessfile2'],
    'UsefulSub': ['useful.txt', 'useful']
  },
  'MixFolder2': {
    '.': ['uselessfile1.txt', 'uselessfile2'],
    'UsefulSub': ['useful.txt', 'useful']
  },
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json'],
        'MixFolder1': {
          '.': ['uselessfile1.txt', 'uselessfile2'],
          'UsefulSub': ['useful.txt', 'useful']
        },
        'MixFolder2': {
          '.': ['uselessfile1.txt', 'uselessfile2'],
          'UsefulSub': ['useful.txt', 'useful']
        }
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  },
  ""publishExclude"": [
    ""UselessFolder1\\**"",
    ""UselessFolder2/**/*"",
    ""UselessFolder3\\**/*.*""
  ]
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""frameworks"": {
    ""dnx451"": {}
  },
  ""publishExclude"": [
    ""UselessFolder1\\**"",
    ""UselessFolder2/**/*"",
    ""UselessFolder3\\**/*.*""
  ]
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}");
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void CorrectlyExcludeFoldersStartingWithDots(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'File', '.FileStartingWithDot', 'File.Having.Dots'],
  '.FolderStaringWithDot': {
    'SubFolder': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    '.SubFolderStartingWithDot': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    'SubFolder.Having.Dots': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    'File': '',
    '.FileStartingWithDot': '',
    'File.Having.Dots': ''
  },
  'Folder': {
    'SubFolder': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    '.SubFolderStartingWithDot': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    'SubFolder.Having.Dots': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    'File': '',
    '.FileStartingWithDot': '',
    'File.Having.Dots': ''
  },
  'Folder.Having.Dots': {
    'SubFolder': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    '.SubFolderStartingWithDot': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    'SubFolder.Having.Dots': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
    'File': '',
    '.FileStartingWithDot': '',
    'File.Having.Dots': ''
  },
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'File', '.FileStartingWithDot', 'File.Having.Dots'],
        'Folder': {
          'SubFolder': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
          'SubFolder.Having.Dots': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
          'File': '',
          '.FileStartingWithDot': '',
          'File.Having.Dots': ''
        },
        'Folder.Having.Dots': {
          'SubFolder': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
          'SubFolder.Having.Dots': ['File', '.FileStartingWithDot', 'File.Having.Dots'],
          'File': '',
          '.FileStartingWithDot': '',
          'File.Having.Dots': ''
        }
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}");
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void VerifyDefaultPublishExcludePatterns(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'File', '.FileStartingWithDot'],
  'bin': {
    'AspNet.Loader.dll': '',
    'Debug': ['test.exe', 'test.dll']
  },
  'obj': {
    'test.obj': '',
    'References': ['ref1.dll', 'ref2.dll']
  },
  '.git': ['index', 'HEAD', 'log'],
  'Folder': {
    '.svn': ['index', 'HEAD', 'log'],
    'File': '',
    '.FileStartingWithDot': ''
  },
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'File', '.FileStartingWithDot'],
        'Folder': ['File', '.FileStartingWithDot']
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}");
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void ProjectWithNoFrameworks(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);
            var projectStructure = @"{
  '.': ['project.json']
}";
            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {}
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                string stdOut;
                string stdErr;
                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    stdOut: out stdOut,
                    stdErr: out stdErr,
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(1, exitCode);
                Assert.Contains("The project being published has no frameworks listed in the 'frameworks' section.", stdErr);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void ProjectWithIncompatibleFrameworks(string flavor, string os, string architecture)
        {
            // Because we're testing cases where the framework in the project is INcompatible with the runtime,
            // this looks backwards. When we're testing against coreclr, we want to write dnx451 into the project and vice versa
            string frameworkInProject = flavor == "coreclr" ? "dnx451" : "dnxcore50";

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);
            var projectStructure = @"{
  '.': ['project.json']
}";
            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {""" + frameworkInProject + @""":{}}
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                string stdOut;
                string stdErr;
                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0} --runtime {1}",
                        testEnv.PublishOutputDirPath,
                        runtimeHomeDir),
                    stdOut: out stdOut,
                    stdErr: out stdErr,
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(1, exitCode);
                Assert.Contains($"The project being published does not support the runtime '{runtimeHomeDir}'", stdErr);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWebApp_CopyExistingWebConfig(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'hosting.json'],
  'public': ['index.html', 'web.config'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'wwwroot': ['web.config', 'index.html'],
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': ['project.json', 'project.lock.json', 'hosting.json']
    }
  }
}".Replace("PROJECT_NAME", _projectName);
            var originalWebConfigContents = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <nonRelatedElement>
    <add key=""non-related-key"" value=""non-related-value"" />
  </nonRelatedElement>
</configuration>";
            var outputWebConfigTemplate = @"<configuration>
  <nonRelatedElement>
    <add key=""non-related-key"" value=""non-related-value"" />
  </nonRelatedElement>
  <system.webServer>
    <handlers>
      <add name=""httpplatformhandler"" path=""*"" verb=""*"" modules=""httpPlatformHandler"" resourceType=""Unspecified"" />
    </handlers>
    <httpPlatform processPath=""..\approot\web.cmd"" arguments="""" stdoutLogEnabled=""true"" stdoutLogFile=""..\logs\stdout.log""></httpPlatform>
  </system.webServer>
</configuration>";

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents("hosting.json", @"{
    ""WebRoot"": ""public""
}")
                    .WithFileContents(Path.Combine("public", "web.config"), originalWebConfigContents)
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0} --wwwroot public --wwwroot-out wwwroot",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "hosting.json"), @"{
    ""WebRoot"": ""../../../wwwroot""
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}")
                    .WithFileContents(Path.Combine("wwwroot", "web.config"), outputWebConfigTemplate, testEnv.ProjectName);
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true, ignoreWhitespace: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWebApp_UpdateExistingWebConfig(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'hosting.json'],
  'public': ['index.html', 'web.config'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'wwwroot': ['web.config', 'index.html'],
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': ['project.json', 'project.lock.json', 'hosting.json']
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            var originalWebConfigContents = @"<configuration>
  <nonRelatedElement>
    <add key=""non-related-key"" value=""OLD_VALUE"" />
  </nonRelatedElement>
  <system.webServer>
    <handlers>
      <add name=""httpplatformhandler"" path=""*"" verb=""*"" modules=""httpPlatformHandler"" resourceType=""Unspecified"" />
    </handlers>
    <httpPlatform processPath=""%DNX_PATH%"" arguments=""%DNX_ARGS%"" stdoutLogEnabled=""false"" rapidFailsPerMinute=""5""></httpPlatform>
  </system.webServer>
</configuration>";

            var outputWebConfigContents = @"<configuration>
  <nonRelatedElement>
    <add key=""non-related-key"" value=""OLD_VALUE"" />
  </nonRelatedElement>
  <system.webServer>
    <handlers>
      <add name=""httpplatformhandler"" path=""*"" verb=""*"" modules=""httpPlatformHandler"" resourceType=""Unspecified"" />
    </handlers>
    <httpPlatform processPath=""..\approot\web.cmd"" arguments="""" stdoutLogEnabled=""false"" stdoutLogFile=""..\logs\stdout.log"" rapidFailsPerMinute=""5""></httpPlatform>
  </system.webServer>
</configuration>";

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("public", "web.config"), originalWebConfigContents)
                    .WithFileContents("hosting.json", @"{
    ""WebRoot"": ""../../../wwwroot""
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0} --wwwroot public --wwwroot-out wwwroot",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""frameworks"": {
    ""dnx451"": {}
  }
}")
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "hosting.json"), @"{
    ""WebRoot"": ""../../../wwwroot""
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}")
                    .WithFileContents(Path.Combine("wwwroot", "web.config"), outputWebConfigContents, testEnv.ProjectName);
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true, ignoreWhitespace: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponentsWithConfigurationOptions))]
        public void GenerateBatchFilesAndBashScriptsWithoutPublishedRuntime(string flavor, string os, string architecture, string configuration)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    '.': ['run.cmd', 'run', 'kestrel.cmd', 'kestrel'],
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json']
      }
    }
  }
}".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""commands"": {
    ""run"": ""run server.urls=http://localhost:5003"",
    ""kestrel"": ""Microsoft.AspNet.Hosting --server Microsoft.AspNet.Server.Kestrel --server.urls http://localhost:5004""
  },
  ""frameworks"": {
    ""dnx451"": { },
    ""dnxcore50"": { }
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var arguments = $"--out {testEnv.PublishOutputDirPath}";
                if (configuration != null)
                {
                    arguments += $" --configuration {configuration}";
                }
                else
                {
                    // default value "Debug" is always set. this variable is used in verification later.
                    configuration = "Debug";
                }

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: arguments,
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""commands"": {
    ""run"": ""run server.urls=http://localhost:5003"",
    ""kestrel"": ""Microsoft.AspNet.Hosting --server Microsoft.AspNet.Server.Kestrel --server.urls http://localhost:5004""
  },
  ""frameworks"": {
    ""dnx451"": { },
    ""dnxcore50"": { }
  }
}")
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.lock.json"), @"{
  ""locked"": false,
  ""version"": LOCKFILEFORMAT_VERSION,
  ""targets"": {
    ""DNX,Version=v4.5.1"": {},
    ""DNXCore,Version=v5.0"": {}
  },
  ""libraries"": {},
  ""projectFileDependencyGroups"": {
    """": [],
    ""DNX,Version=v4.5.1"": [],
    ""DNXCore,Version=v5.0"": []
  }
}".Replace("LOCKFILEFORMAT_VERSION", Constants.LockFileVersion.ToString()))
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}")
                    .WithFileContents(Path.Combine("approot", "run.cmd"), BatchFileTemplate, string.Empty, Constants.BootstrapperExeName, testEnv.ProjectName, configuration, "run")
                    .WithFileContents(Path.Combine("approot", "kestrel.cmd"), BatchFileTemplate, string.Empty, Constants.BootstrapperExeName, testEnv.ProjectName, configuration, "kestrel")
                    .WithFileContents(Path.Combine("approot", "run"),
                        BashScriptTemplate, testEnv.ProjectName, string.Empty, Constants.BootstrapperExeName, configuration, "run")
                    .WithFileContents(Path.Combine("approot", "kestrel"),
                        BashScriptTemplate, testEnv.ProjectName, string.Empty, Constants.BootstrapperExeName, configuration, "kestrel");

                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponentsWithConfigurationOptions))]
        public void GenerateBatchFilesAndBashScriptsWithPublishedRuntime(string flavor, string os, string architecture, string configuration)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            // Each runtime home only contains one runtime package, which is the one we are currently testing against
            var runtimeRoot = Directory.EnumerateDirectories(Path.Combine(runtimeHomeDir, "runtimes"), Constants.RuntimeNamePrefix + "*").First();
            var runtimeName = new DirectoryInfo(runtimeRoot).Name;

            var projectStructure = @"{
  '.': ['project.json'],
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    '.': ['run.cmd', 'run', 'kestrel.cmd', 'kestrel'],
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json']
      }
    },
    'packages': {},
    'runtimes': {
      'RUNTIME_PACKAGE_NAME': {}
    }
  }
}".Replace("PROJECT_NAME", _projectName).Replace("RUNTIME_PACKAGE_NAME", runtimeName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""commands"": {
    ""run"": ""run server.urls=http://localhost:5003"",
    ""kestrel"": ""Microsoft.AspNet.Hosting --server Microsoft.AspNet.Server.Kestrel --server.urls http://localhost:5004""
  },
  ""frameworks"": {
    ""dnx451"": { },
    ""dnxcore50"": { }
  }
}")
                    .WriteTo(testEnv.ProjectPath);

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") },
                    { EnvironmentNames.Home, runtimeHomeDir },
                    { EnvironmentNames.Trace, "1" }
                };

                var arguments = $"--out {testEnv.PublishOutputDirPath} --runtime {runtimeName}";
                if (configuration != null)
                {
                    arguments += $" --configuration {configuration}";
                }
                else
                {
                    // default value "Debug" is always set. this variable is used in verification later.
                    configuration = "Debug";
                }

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: arguments,
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var runtimeSubDir = DirTree.CreateFromDirectory(runtimeRoot)
                    .RemoveFile(Path.Combine("bin", "lib", "Microsoft.Dnx.Tooling",
                        "bin", "profile", "startup.prof"));

                var batchFileBinPath = string.Format(@"%~dp0runtimes\{0}\bin\", runtimeName);
                var bashScriptBinPath = string.Format("$DIR/runtimes/{0}/bin/", runtimeName);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""commands"": {
    ""run"": ""run server.urls=http://localhost:5003"",
    ""kestrel"": ""Microsoft.AspNet.Hosting --server Microsoft.AspNet.Server.Kestrel --server.urls http://localhost:5004""
  },
  ""frameworks"": {
    ""dnx451"": { },
    ""dnxcore50"": { }
  }
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}")
                    .WithFileContents(Path.Combine("approot", "run.cmd"), BatchFileTemplate, runtimeName, Constants.BootstrapperExeName, testEnv.ProjectName, configuration, "run")
                    .WithFileContents(Path.Combine("approot", "kestrel.cmd"), BatchFileTemplate, runtimeName, Constants.BootstrapperExeName, testEnv.ProjectName, configuration, "kestrel")
                    .WithFileContents(Path.Combine("approot", "run"),
                        BashScriptTemplate, testEnv.ProjectName, bashScriptBinPath, Constants.BootstrapperExeName, configuration, "run")
                    .WithFileContents(Path.Combine("approot", "kestrel"),
                        BashScriptTemplate, testEnv.ProjectName, bashScriptBinPath, Constants.BootstrapperExeName, configuration, "kestrel")
                    .WithSubDir(Path.Combine("approot", "runtimes", runtimeName), runtimeSubDir);

                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));

                var lockFile = new LockFileReader().Read(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"));
                var fx = flavor == "coreclr" ? DnxCore50 : Dnx451;
                Assert.True(lockFile.HasTarget(fx));

                // GetRuntimeIdentifiers is tested separately, we're testing that the targets made the lock file here.
                var rids = Publish.DependencyContext.GetRuntimeIdentifiers(runtimeName);
                foreach(var rid in rids)
                {
                    Assert.True(lockFile.HasTarget(fx, rid));
                }
            }
        }

        [Theory]
        [MemberData("ClrRuntimeComponents")]
        public void PublishWithNoSourceOptionGeneratesLockFileOnClr(string flavor, string os, string architecture)
        {
            const string testApp = "NoDependencies";
            string expectedOutputStructure = @"{
  'approot': {
    '.': ['hello', 'hello.cmd'],
    'global.json': '',
    'packages': {
      'NoDependencies': {
        '1.0.0': {
          '.': ['NoDependencies.1.0.0.nupkg', 'NoDependencies.1.0.0.nupkg.sha512', 'NoDependencies.nuspec'],
          'app': ['hello', 'hello.cmd', 'project.json'],
          'root': ['project.json', 'project.lock.json'],
          'lib': {
            'dnx451': ['NoDependencies.dll', 'NoDependencies.xml']
          }
        }
      }
    }
  }
}";

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var tempDir = new DisposableDir())
            {
                var publishOutputPath = Path.Combine(tempDir, "output");
                var appPath = Path.Combine(tempDir, testApp);
                TestUtils.CopyFolder(TestUtils.GetXreTestAppPath(testApp), appPath);

                var exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", appPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--no-source --out {0}", publishOutputPath),
                    environment: null,
                    workingDir: appPath);

                Assert.Equal(0, exitCode);

                Assert.True(DirTree.CreateFromJson(expectedOutputStructure)
                    .MatchDirectoryOnDisk(publishOutputPath, compareFileContents: false));

                var outputLockFilePath = Path.Combine(publishOutputPath,
                    "approot", "packages", testApp, "1.0.0", "root", LockFileFormat.LockFileName);

                var lockFile = new LockFileReader().Read(outputLockFilePath);
                Assert.True(lockFile.PackageLibraries.Any(p => p.Name.Equals("NoDependencies")));
            }
        }

        [Theory]
        [MemberData("ClrRuntimeComponents")]
        public void PublishWithNoSourceOptionUpdatesLockFileOnClr(string flavor, string os, string architecture)
        {
            const string testApp = "NoDependencies";
            string expectedOutputStructure = @"{
  'approot': {
    '.': ['hello', 'hello.cmd'],
    'global.json': '',
    'packages': {
      'NoDependencies': {
        '1.0.0': {
          '.': ['NoDependencies.1.0.0.nupkg', 'NoDependencies.1.0.0.nupkg.sha512', 'NoDependencies.nuspec'],
          'app': ['hello', 'hello.cmd', 'project.json'],
          'root': ['project.json', 'project.lock.json'],
          'lib': {
            'dnx451': ['NoDependencies.dll', 'NoDependencies.xml']
          }
        }
      }
    }
  }
}";

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var tempDir = new DisposableDir())
            {
                var publishOutputPath = Path.Combine(tempDir, "output");
                var appPath = Path.Combine(tempDir, testApp);
                TestUtils.CopyFolder(TestUtils.GetXreTestAppPath(testApp), appPath);

                // Generate lockfile for the HelloWorld app
                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "restore",
                    arguments: string.Empty,
                    environment: null,
                    workingDir: appPath);

                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--no-source --out {0}", publishOutputPath),
                    environment: null,
                    workingDir: appPath);

                Assert.Equal(0, exitCode);

                Assert.True(DirTree.CreateFromJson(expectedOutputStructure)
                    .MatchDirectoryOnDisk(publishOutputPath, compareFileContents: false));

                var outputLockFilePath = Path.Combine(publishOutputPath,
                    "approot", "packages", testApp, "1.0.0", "root", LockFileFormat.LockFileName);

                var lockFile = new LockFileReader().Read(outputLockFilePath);
                Assert.True(lockFile.PackageLibraries.Any(p => p.Name.Equals("NoDependencies")));
            }
        }

        [ConditionalTheory]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono)]
        [MemberData(nameof(RuntimeComponents))]
        public void PublishWithIncludeSymbolsOptionIncludesSymbolsAndSourceCode(string flavor, string os, string architecture)
        {
            const string testApp = "NoDependencies";
            string expectedOutputStructure = @"{
  'approot': {
    '.': ['hello', 'hello.cmd'],
    'global.json': '',
    'packages': {
      'NoDependencies': {
        '1.0.0': {
          '.': ['NoDependencies.1.0.0.nupkg', 'NoDependencies.1.0.0.nupkg.sha512', 'NoDependencies.nuspec'],
          'root': ['project.json', 'LOCKFILE_NAME'],
          'src': {
            'NoDependencies': [ 'Program.cs' ]
          },
          'lib': {
            'dnx451': ['NoDependencies.dll', 'NoDependencies.pdb', 'NoDependencies.xml']
          }
        }
      }
    }
  }
}".Replace("LOCKFILE_NAME", LockFileFormat.LockFileName);

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var tempDir = new DisposableDir())
            {
                var publishOutputPath = Path.Combine(tempDir, "output");
                var appPath = Path.Combine(tempDir, testApp);
                TestUtils.CopyFolder(TestUtils.GetXreTestAppPath(testApp), appPath);

                var lockFilePath = Path.Combine(appPath, LockFileFormat.LockFileName);
                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }

                var exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", appPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: $"--no-source --include-symbols --out {publishOutputPath}",
                    environment: null,
                    workingDir: appPath);

                Assert.Equal(0, exitCode);

                Assert.True(DirTree.CreateFromJson(expectedOutputStructure)
                    .MatchDirectoryOnDisk(publishOutputPath, compareFileContents: false));
            }
        }

        [ConditionalTheory]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono)]
        [MemberData(nameof(ClrRuntimeComponents))]
        public void CanPublishWrappedProjectReference(string flavor, string os, string architecture)
        {
            // ConsoleAppReferencingWrappedProject references Net45Library in its project.json
            const string testAppName = "ConsoleAppReferencingWrappedProject";
            const string referenceProjectName = "Net45Library";
            const string configuration = "Debug";

            // To run a published bundle, we should use its own packages folder, which is specified in global.json
            // However, some environment variables can override packages folder location
            // We need to erase those envrionment variables before running the output app
            var env = new Dictionary<string, string>
            {
                { EnvironmentNames.Packages, null },
                { EnvironmentNames.DnxPackages, null }
            };

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var tempDir = new DisposableDir())
            {
                var outputPath = Path.Combine(tempDir, "output");
                var solutionRootPath = Path.Combine(tempDir, testAppName);
                var mainProjectPath = Path.Combine(solutionRootPath, "src", testAppName);
                var referenceProjectFilePath = Path.Combine(
                    solutionRootPath,
                    "src",
                    referenceProjectName,
                    $"{referenceProjectName}.csproj");
                TestUtils.CopyFolder(TestUtils.GetDnuPublishTestAppPath(testAppName), solutionRootPath);

                // First generate assemblies that will be referenced by wrapper project
                var exitCode = TestUtils.BuildCsProject(referenceProjectFilePath, configuration);
                Assert.Equal(0, exitCode);

                // "dnu wrap" the csproj to generate wrapper project
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "wrap", referenceProjectFilePath);
                Assert.Equal(0, exitCode);

                // Generate lock file before publishing the main project
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", mainProjectPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: $"publish {mainProjectPath}",
                    arguments: $"--out {outputPath} --configuration {configuration}");
                Assert.Equal(0, exitCode);

                // The wrapper project should be packed to a nupkg
                Assert.False(Directory.Exists(Path.Combine(outputPath, "approot", "src", referenceProjectName)));
                Assert.True(Directory.Exists(Path.Combine(outputPath, "approot", "packages", referenceProjectName)));

                // The output bundled app should be runnable
                var outputMainAppPath = Path.Combine(outputPath, "approot", "src", testAppName);
                string stdOut, stdErr;
                exitCode = TestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: $"-p {outputMainAppPath} run",
                    stdOut: out stdOut,
                    stdErr: out stdErr,
                    environment: env);
                Assert.Equal(0, exitCode);
            }
        }

        [Theory(Skip = "Creating long path file failed on Windows Server 2012 R2")]
        [MemberData(nameof(RuntimeComponents))]
        public void PublishExcludeWithLongPath(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure = @"{
  '.': ['project.json', 'Config.json', 'Program.cs'],
  'Data': {
    'Input': ['data1.dat', 'data2.dat'],
    'Backup': ['backup1.dat', 'backup2.dat']
  },
  'packages': {}
}";
            var expectedOutputStructure = @"{
  'approot': {
    'global.json': '',
    'src': {
      'PROJECT_NAME': {
        '.': ['project.json', 'project.lock.json', 'Config.json', 'Program.cs'],
          'Data': {
            'Input': ['data1.dat', 'data2.dat']
          }
        }
      }
    }
  }".Replace("PROJECT_NAME", _projectName);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir, _projectName, _outputDirName))
            {
                DirTree.CreateFromJson(projectStructure)
                    .WithFileContents("project.json", @"{
  ""publishExclude"": ""Data/Backup/**"",
  ""exclude"": ""node_modules""
}")
                    .WriteTo(testEnv.ProjectPath);

                BuildLongPath(Path.Combine(testEnv.ProjectPath, "node_modules"));

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(testEnv.ProjectPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--out {0}",
                        testEnv.PublishOutputDirPath),
                    environment: environment,
                    workingDir: testEnv.ProjectPath);
                Assert.Equal(0, exitCode);

                var expectedOutputDir = DirTree.CreateFromJson(expectedOutputStructure)
                    .WithFileContents(Path.Combine("approot", "src", testEnv.ProjectName, "project.json"), @"{
  ""publishExclude"": ""Data/Backup/**"",
  ""exclude"": ""node_modules""
}")
                    .WithFileContents(Path.Combine("approot", "global.json"), @"{
  ""projects"": [
    ""src""
  ],
  ""packages"": ""packages""
}");
                Assert.True(expectedOutputDir.MatchDirectoryOnDisk(testEnv.PublishOutputDirPath,
                    compareFileContents: true));
                AssertDefaultTargets(Path.Combine(testEnv.PublishOutputDirPath, "approot", "src", testEnv.ProjectName, "project.lock.json"), Dnx451);
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void PublishWithNoSourceOption_AppHasResourceFile(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            using (var tempDir = new DisposableDir())
            {
                var publishOutputPath = Path.Combine(tempDir, "output");
                var appPath = Path.Combine(tempDir, "ResourcesTestProjects", "ReadFromResources");
                TestUtils.CopyFolder(Path.Combine(TestUtils.GetMiscProjectsFolder(), "ResourcesTestProjects", "ReadFromResources"), appPath);
                var workingDir = Path.Combine(appPath, "src", "ReadFromResources");

                // Restore the application
                var exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", appPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: string.Format("--no-source --out {0}", publishOutputPath),
                    environment: null,
                    workingDir: workingDir);

                Assert.Equal(0, exitCode);

                var appOutputPath = Path.Combine(publishOutputPath, "approot", "packages", "ReadFromResources");
                var versionDir = new DirectoryInfo(appOutputPath).GetDirectories().First().FullName;

                Assert.True(File.Exists(Path.Combine(versionDir,
                    "lib", "dnx451", "fr-FR", "ReadFromResources.resources.dll")), "Resources assembly did not get published for dnx451");
                Assert.True(File.Exists(Path.Combine(versionDir,
                    "lib", "dnxcore50", "fr-FR", "ReadFromResources.resources.dll")), "Resources assembly did not get published for dnxcore50");

                appOutputPath = Path.Combine(publishOutputPath, "approot", "packages", "Microsoft.Data.Edm");
                versionDir = new DirectoryInfo(appOutputPath).GetDirectories().First().FullName;
                var edmLocales = new List<string>() { "de", "es", "fr", "it", "ja", "ko", "ru", "zh-Hans", "zh-Hant" };
                var edmFxs = new List<string>() { "net40", "portable-net45+wp8+win8+wpa" };

                foreach (var fx in edmFxs)
                {
                    foreach (var locale in edmLocales)
                    {
                        Assert.True(File.Exists(Path.Combine(versionDir, "lib", fx, locale, "Microsoft.Data.Edm.resources.dll")),
                            string.Format("Microsoft.Data.Edm {0} resources assembly did not get published for {1}", locale, fx));
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWithSpecificFramework_dnx451(string flavor, string os, string architecture)
        {
            DnuPublish_SpecificFramework(flavor, os, architecture, "dnx451");
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuPublishWithSpecificFramework_dnxcore50(string flavor, string os, string architecture)
        {
            DnuPublish_SpecificFramework(flavor, os, architecture, "dnxcore50");
        }

        private void DnuPublish_SpecificFramework(string flavor, string os, string architecture, string targetFramework)
        {
            const string ProjectAName = "proj.A";
            const string ProjectBName = "proj.B";
            const string PublishFolderName = "publish";

            var supportedFrameworks = new string[] { "dnx451", "dnxcore50" };

            var runtimeHomeDir = _fixture.GetRuntimeHomeDir(flavor, os, architecture);

            var projectStructure =
@"{
  '.': ['project.json', 'Program.cs'],
  'packages': {}
}";
            using (var solutionFolder = new DisposableDir())
            {
                var projectAFolder = Path.Combine(solutionFolder.DirPath, ProjectAName);
                var projectBFolder = Path.Combine(solutionFolder.DirPath, ProjectBName);
                var publishFolder = Path.Combine(solutionFolder.DirPath, PublishFolderName);

                DirTree
                   .CreateFromJson(projectStructure)
                   .WithFileContents("project.json",
@"{
  ""dependencies"": {
  },
  ""frameworks"": {
    ""dnx451"": {},
    ""dnxcore50"": {
      ""dependencies"": {
        ""System.Runtime"":""4.0.20-*""
      }
    }
  }
}")
                   .WriteTo(projectAFolder);

                DirTree
                    .CreateFromJson(projectStructure)
                    .WithFileContents("project.json",
@"{
  ""dependencies"": {
    """ + ProjectAName + @""": ""1.0.0-*""
  },
  ""frameworks"": {
    ""dnx451"": {},
    ""dnxcore50"": {
      ""dependencies"": {
        ""System.Runtime"":""4.0.10-*""
      }
    }
  }
}")
                    .WriteTo(projectBFolder);

                File.WriteAllText(Path.Combine(solutionFolder.DirPath, "global.json"),
@"{
    ""projects"": ["".""]
}");
                File.WriteAllText(Path.Combine(solutionFolder.DirPath, "NuGet.config"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key = ""NuGet"" value = ""https://nuget.org/api/v2/"" />
  </packageSources>
</configuration>");

                var environment = new Dictionary<string, string>()
                {
                    { EnvironmentNames.Packages, Path.Combine(solutionFolder.DirPath, "packages") }
                };

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "restore",
                    arguments: "",
                    environment: environment,
                    workingDir: solutionFolder.DirPath);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    subcommand: "publish",
                    arguments: $"--out {publishFolder} --framework {targetFramework} --no-source",
                    environment: environment,
                    workingDir: projectBFolder);
                Assert.Equal(0, exitCode);

                var packagesFolder = Path.Combine(solutionFolder.DirPath, publishFolder, "approot", "packages");
                var frameworksNotExpected = supportedFrameworks
                    .Except(new string[] { targetFramework })
                    .ToList();

                foreach (var singlePackageFolder in Directory.EnumerateDirectories(packagesFolder, "dnx*", SearchOption.AllDirectories))
                {
                    var folderName = Path.GetFileName(singlePackageFolder);
                    Assert.False(frameworksNotExpected.Contains(folderName));
                }
            }
        }

        private string BuildLongPath(string baseDir)
        {
            const int maxPath = 248;
            var resultPath = baseDir;

            string randomFilename;
            string newpath;
            while (true)
            {
                randomFilename = Path.GetRandomFileName();
                newpath = string.Format("{0}{1}{2}", resultPath, Path.DirectorySeparatorChar, randomFilename);

                if (newpath.Length > maxPath)
                {
                    break;
                }
                else
                {
                    resultPath = newpath;
                }
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.CreateDirectory(resultPath);
                Directory.SetCurrentDirectory(resultPath);
                File.WriteAllText(randomFilename, "wow");
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
            }

            return resultPath;
        }

        private static void AssertDefaultTargets(string lockFilePath, FrameworkName framework)
        {
            var lockFile = new LockFileReader().Read(lockFilePath);
            Assert.True(lockFile.HasTarget(framework));
            foreach(var rid in PlatformServices.Default.Runtime.GetDefaultRestoreRuntimes())
            {
                Assert.True(lockFile.HasTarget(framework, rid));
            }
        }
    }
}

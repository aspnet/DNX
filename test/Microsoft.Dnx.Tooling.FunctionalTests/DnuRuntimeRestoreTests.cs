﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.AspNet.Testing.xunit;
using Microsoft.Dnx.CommonTestUtils;
using Microsoft.Dnx.Runtime;
using Microsoft.Extensions.PlatformAbstractions;
using Newtonsoft.Json.Linq;
using NuGet;
using Xunit;

namespace Microsoft.Dnx.Tooling.FunctionalTests
{
    public class DnuRuntimeRestoreTests
    {
        private static readonly FrameworkName Dnx451 = VersionUtility.ParseFrameworkName("dnx451");

        public static IEnumerable<object[]> RuntimeComponents
        {
            get
            {
                return TestUtils.GetRuntimeComponentsCombinations();
            }
        }

        [Theory(Skip = "Test has been really flaky recently. Manual testing covers this well right now")]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuRestore_GeneratesDefaultRuntimeTargets(string flavor, string os, string architecture)
        {
            // TODO(anurse): Maybe this could be a condition? This is the only place we need it right now so it
            // didn't seem worth the refactor.
            // Travis has old versions of OSes and our test package doesn't work there
            var isTravisEnvironment = Environment.GetEnvironmentVariable("TRAVIS") ?? "false";
            if (isTravisEnvironment.Equals("true"))
            {
                return;
            }

            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);

            LockFile lockFile;
            using (var testDir = new DisposableDir())
            {
                var misc = TestUtils.GetMiscProjectsFolder();
                DirTree.CreateFromDirectory(Path.Combine(misc, "RuntimeRestore", "TestProject"))
                    .WriteTo(testDir);

                // Clean up the lock file if it ended up there during the copy
                var lockFilePath = Path.Combine(testDir, "project.lock.json");
                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }

                // Restore the project!
                var source = Path.Combine(misc, "RuntimeRestore", "RuntimeRestoreTestPackage", "feed");
                DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", $"--source {source}", workingDir: testDir);

                // Check the lock file
                lockFile = (new LockFileFormat()).Read(Path.Combine(testDir, "project.lock.json"));
            }

            // We can use the runtime environment to determine the expected RIDs
            var osName = RuntimeEnvironmentHelper.RuntimeEnvironment.GetDefaultRestoreRuntimes().First();
            if (osName.StartsWith("win"))
            {
                AssertLockFileTarget(lockFile, "win7-x86", "win7-x86");
                AssertLockFileTarget(lockFile, "win7-x64", "win7-x64");
            }
            else if (osName.StartsWith("ubuntu"))
            {
                // There is only ubuntu 14.04 in the package
                AssertLockFileTarget(lockFile, osName + "-x86", assemblyRid: null); // There is no ubuntu/osx-x86 in the test package
                AssertLockFileTarget(lockFile, osName + "-x64", "ubuntu.14.04-x64");
            }
            else if (osName.StartsWith("osx"))
            {
                // There is only osx 10.10 in the package
                AssertLockFileTarget(lockFile, osName + "-x86", assemblyRid: null); // There is no ubuntu/osx-x86 in the test package
                AssertLockFileTarget(lockFile, osName + "-x64", "osx.10.10-x64");
            }
            else
            {
                Assert.True(false, $"Unknown OS Name: {osName}");
            }
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuRestore_UsesProjectAndCommandLineProvidedRuntimes(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);

            LockFile lockFile;
            using (var testDir = new DisposableDir())
            {
                var misc = TestUtils.GetMiscProjectsFolder();
                DirTree.CreateFromDirectory(Path.Combine(misc, "RuntimeRestore", "TestProject"))
                    .WriteTo(testDir);

                // Clean up the lock file if it ended up there during the copy
                var lockFilePath = Path.Combine(testDir, "project.lock.json");
                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }

                // Modify the project
                AddRuntimeToProject(testDir, "win10-x86");

                // Restore the project!
                var source = Path.Combine(misc, "RuntimeRestore", "RuntimeRestoreTestPackage", "feed");
                DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", $"--source {source} --runtime ubuntu.14.04-x64 --runtime osx.10.10-x64", workingDir: testDir);

                // Check the lock file
                lockFile = (new LockFileFormat()).Read(Path.Combine(testDir, "project.lock.json"));
            }

            AssertLockFileTarget(lockFile, "win10-x86", "win8-x86");
            AssertLockFileTarget(lockFile, "osx.10.10-x64", "osx.10.10-x64");
            AssertLockFileTarget(lockFile, "ubuntu.14.04-x64", "ubuntu.14.04-x64");
        }

        [Theory]
        [MemberData(nameof(RuntimeComponents))]
        public void DnuRestore_DoesFallback(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);

            LockFile lockFile;
            using (var testDir = new DisposableDir())
            {
                var misc = TestUtils.GetMiscProjectsFolder();
                DirTree.CreateFromDirectory(Path.Combine(misc, "RuntimeRestore", "TestProject"))
                    .WriteTo(testDir);

                // Clean up the lock file if it ended up there during the copy
                var lockFilePath = Path.Combine(testDir, "project.lock.json");
                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }

                // Restore the project!
                var source = Path.Combine(misc, "RuntimeRestore", "RuntimeRestoreTestPackage", "feed");
                DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", $"--source {source} --runtime win10-x64 --runtime win10-x86 --runtime ubuntu.14.04-x86", workingDir: testDir);

                // Check the lock file
                lockFile = (new LockFileFormat()).Read(Path.Combine(testDir, "project.lock.json"));
            }

            AssertLockFileTarget(lockFile, "win10-x64", "win7-x64");
            AssertLockFileTarget(lockFile, "win10-x86", "win8-x86");
            AssertLockFileTarget(lockFile, "ubuntu.14.04-x86", assemblyRid: null);
        }

        private void AddRuntimeToProject(string projectRoot, string rid)
        {
            var projectFile = Path.Combine(projectRoot, "project.json");
            var json = JObject.Parse(File.ReadAllText(projectFile));
            json["runtimes"] = new JObject(new JProperty(rid, new JObject()));
            File.WriteAllText(projectFile, json.ToString());
        }

        private void AssertLockFileTarget(LockFile lockFile, string searchRid, string assemblyRid)
        {
            var target = lockFile.Targets.SingleOrDefault(t => t.TargetFramework == Dnx451 && string.Equals(t.RuntimeIdentifier, searchRid, StringComparison.Ordinal));
            Assert.NotNull(target);
            var library = target.Libraries.SingleOrDefault(l => l.Name.Equals("RuntimeRestoreTest"));
            Assert.NotNull(library);

            if (string.IsNullOrEmpty(assemblyRid))
            {
                AssertLockFileItemPath("lib/dnx451/RuntimeRestoreTest.dll", library.CompileTimeAssemblies.Single());
                AssertLockFileItemPath("lib/dnx451/RuntimeRestoreTest.dll", library.RuntimeAssemblies.Single());
            }
            else
            {
                AssertLockFileItemPath($"runtimes/{assemblyRid}/lib/dnx451/RuntimeRestoreTest.dll", library.CompileTimeAssemblies.Single());
                AssertLockFileItemPath($"runtimes/{assemblyRid}/lib/dnx451/RuntimeRestoreTest.dll", library.RuntimeAssemblies.Single());
            }
        }

        private void AssertLockFileItemPath(string path, LockFileItem item)
        {
            Assert.NotNull(item);
            Assert.Equal(path, PathUtility.GetPathWithForwardSlashes(item.Path));
        }
    }
}

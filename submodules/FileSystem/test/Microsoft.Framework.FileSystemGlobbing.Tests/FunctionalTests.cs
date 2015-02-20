﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.AspNet.Testing.xunit;
using Microsoft.Framework.FileSystemGlobbing.Abstractions;
using Microsoft.Framework.FileSystemGlobbing.Tests.TestUtility;
using Xunit;

namespace Microsoft.Framework.FileSystemGlobbing.Tests
{
    public class FunctionalTests : IDisposable
    {
        private DisposableFileSystem _context;

        public FunctionalTests()
        {
            _context = CreateContext();
        }

        public void Dispose()
        {
            if (_context != null)
            {
                _context.Dispose();
            }
        }

        [Fact]
        public void RecursiveAndDoubleParentsWithRecursiveSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude("**/*.cs")
                   .AddInclude(@"../../lib/**/*.cs");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project/source1.cs",
                "src/project/sub/source2.cs",
                "src/project/sub/source3.cs",
                "src/project/sub2/source4.cs",
                "src/project/sub2/source5.cs",
                "src/project/compiler/preprocess/preprocess-source1.cs",
                "src/project/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project/compiler/shared/shared1.cs",
                "src/project/compiler/shared/sub/shared2.cs",
                "src/project/compiler/shared/sub/sub/sharedsub.cs",
                "lib/source6.cs",
                "lib/sub3/source7.cs",
                "lib/sub4/source8.cs");
        }

        [Fact]
        public void RecursiveAndDoubleParentsSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude("**/*.cs")
                   .AddInclude(@"../../lib/*.cs");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project/source1.cs",
                "src/project/sub/source2.cs",
                "src/project/sub/source3.cs",
                "src/project/sub2/source4.cs",
                "src/project/sub2/source5.cs",
                "src/project/compiler/preprocess/preprocess-source1.cs",
                "src/project/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project/compiler/shared/shared1.cs",
                "src/project/compiler/shared/sub/shared2.cs",
                "src/project/compiler/shared/sub/sub/sharedsub.cs",
                "lib/source6.cs");
        }

        [Fact]
        public void WildcardAndDoubleParentWithRecursiveSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"..\..\lib\**\*.cs");
            matcher.AddInclude(@"*.cs");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project/source1.cs",
                "lib/source6.cs",
                "lib/sub3/source7.cs",
                "lib/sub4/source8.cs");
        }

        [Fact]
        public void WildcardAndDoubleParentsSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"..\..\lib\*.cs");
            matcher.AddInclude(@"*.cs");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project/source1.cs",
                "lib/source6.cs");
        }

        [Fact]
        public void DoubleParentsWithRecursiveSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"..\..\lib\**\*.cs");

            ExecuteAndVerify(matcher, @"src/project",
                "lib/source6.cs",
                "lib/sub3/source7.cs",
                "lib/sub4/source8.cs");
        }

        [Fact]
        public void OneLevelParentAndRecursiveSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"../project2/**/*.cs");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project2/source1.cs",
                "src/project2/sub/source2.cs",
                "src/project2/sub/source3.cs",
                "src/project2/sub2/source4.cs",
                "src/project2/sub2/source5.cs",
                "src/project2/compiler/preprocess/preprocess-source1.cs",
                "src/project2/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project2/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project2/compiler/shared/shared1.cs",
                "src/project2/compiler/shared/sub/shared2.cs",
                "src/project2/compiler/shared/sub/sub/sharedsub.cs");
        }

        [Fact]
        public void RecursiveSuffixSearch()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"**.txt");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.txt",
                "src/project/compiler/shared/shared1.txt",
                "src/project/compiler/shared/sub/shared2.txt",
                "src/project/content1.txt");
        }

        [Fact]
        public void FolderExclude()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"**/*.*");
            matcher.AddExclude(@"obj");
            matcher.AddExclude(@"bin");
            matcher.AddExclude(@".*");

            ExecuteAndVerify(matcher, @"src/project",
                "src/project/source1.cs",
                "src/project/sub/source2.cs",
                "src/project/sub/source3.cs",
                "src/project/sub2/source4.cs",
                "src/project/sub2/source5.cs",
                "src/project/compiler/preprocess/preprocess-source1.cs",
                "src/project/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.txt",
                "src/project/compiler/shared/shared1.cs",
                "src/project/compiler/shared/shared1.txt",
                "src/project/compiler/shared/sub/shared2.cs",
                "src/project/compiler/shared/sub/shared2.txt",
                "src/project/compiler/shared/sub/sub/sharedsub.cs",
                "src/project/compiler/resources/resource.res",
                "src/project/compiler/resources/sub/resource2.res",
                "src/project/compiler/resources/sub/sub/resource3.res",
                "src/project/content1.txt");
        }

        [Fact]
        public void FolderInclude()
        {
            var matcher = new Matcher();
            matcher.AddInclude(@"compiler/");
            ExecuteAndVerify(matcher, @"src/project",
                "src/project/compiler/preprocess/preprocess-source1.cs",
                "src/project/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.txt",
                "src/project/compiler/shared/shared1.cs",
                "src/project/compiler/shared/shared1.txt",
                "src/project/compiler/shared/sub/shared2.cs",
                "src/project/compiler/shared/sub/shared2.txt",
                "src/project/compiler/shared/sub/sub/sharedsub.cs",
                "src/project/compiler/resources/resource.res",
                "src/project/compiler/resources/sub/resource2.res",
                "src/project/compiler/resources/sub/sub/resource3.res");
        }

        [Theory]
        [InlineData("source1.cs", "src/project/source1.cs")]
        [InlineData("../project2/source1.cs", "src/project2/source1.cs")]
        public void SingleFile(string pattern, string expect)
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            ExecuteAndVerify(matcher, "src/project", expect);
        }

        [Fact]
        public void SingleFileAndRecursive()
        {
            var matcher = new Matcher();
            matcher.AddInclude("**/*.cs");
            matcher.AddInclude("../project2/source1.cs");
            ExecuteAndVerify(matcher, "src/project",
                "src/project/source1.cs",
                "src/project/sub/source2.cs",
                "src/project/sub/source3.cs",
                "src/project/sub2/source4.cs",
                "src/project/sub2/source5.cs",
                "src/project/compiler/preprocess/preprocess-source1.cs",
                "src/project/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project/compiler/shared/shared1.cs",
                "src/project/compiler/shared/sub/shared2.cs",
                "src/project/compiler/shared/sub/sub/sharedsub.cs",
                "src/project2/source1.cs");
        }

#if ASPNETCORE50
        [ConditionalFact]
        [RunWhenWhenDirectoryInfoWorks]
        public void WarningTest()
        {

        }
#endif

        private DisposableFileSystem CreateContext()
        {
            var context = new DisposableFileSystem();
            context.CreateFiles(
                "src/project/source1.cs",
                "src/project/sub/source2.cs",
                "src/project/sub/source3.cs",
                "src/project/sub2/source4.cs",
                "src/project/sub2/source5.cs",
                "src/project/compiler/preprocess/preprocess-source1.cs",
                "src/project/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project/compiler/preprocess/sub/sub/preprocess-source3.txt",
                "src/project/compiler/shared/shared1.cs",
                "src/project/compiler/shared/shared1.txt",
                "src/project/compiler/shared/sub/shared2.cs",
                "src/project/compiler/shared/sub/shared2.txt",
                "src/project/compiler/shared/sub/sub/sharedsub.cs",
                "src/project/compiler/resources/resource.res",
                "src/project/compiler/resources/sub/resource2.res",
                "src/project/compiler/resources/sub/sub/resource3.res",
                "src/project/content1.txt",
                "src/project/obj/object.o",
                "src/project/bin/object",
                "src/project/.hidden/file1.hid",
                "src/project/.hidden/sub/file2.hid",
                "src/project2/source1.cs",
                "src/project2/sub/source2.cs",
                "src/project2/sub/source3.cs",
                "src/project2/sub2/source4.cs",
                "src/project2/sub2/source5.cs",
                "src/project2/compiler/preprocess/preprocess-source1.cs",
                "src/project2/compiler/preprocess/sub/preprocess-source2.cs",
                "src/project2/compiler/preprocess/sub/sub/preprocess-source3.cs",
                "src/project2/compiler/preprocess/sub/sub/preprocess-source3.txt",
                "src/project2/compiler/shared/shared1.cs",
                "src/project2/compiler/shared/shared1.txt",
                "src/project2/compiler/shared/sub/shared2.cs",
                "src/project2/compiler/shared/sub/shared2.txt",
                "src/project2/compiler/shared/sub/sub/sharedsub.cs",
                "src/project2/compiler/resources/resource.res",
                "src/project2/compiler/resources/sub/resource2.res",
                "src/project2/compiler/resources/sub/sub/resource3.res",
                "src/project2/content1.txt",
                "src/project2/obj/object.o",
                "src/project2/bin/object",
                "lib/source6.cs",
                "lib/sub3/source7.cs",
                "lib/sub4/source8.cs",
                "res/resource1.text",
                "res/resource2.text",
                "res/resource3.text",
                ".hidden/file1.hid",
                ".hidden/sub/file2.hid");

            return context;
        }

        private void ExecuteAndVerify(Matcher matcher, string directoryPath, params string[] expectFiles)
        {
            directoryPath = Path.Combine(_context.RootPath, directoryPath);
            var results = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(directoryPath)));

            var actual = results.Files.Select(relativePath => Path.GetFullPath(Path.Combine(_context.RootPath, directoryPath, relativePath)));
            var expect = expectFiles.Select(relativePath => Path.GetFullPath(Path.Combine(_context.RootPath, relativePath)));

            AssertHelpers.SortAndEqual(expect, actual, StringComparer.OrdinalIgnoreCase);
        }
    }
}
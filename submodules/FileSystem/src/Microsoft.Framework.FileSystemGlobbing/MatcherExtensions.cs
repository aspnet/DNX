// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Framework.FileSystemGlobbing.Abstractions;

namespace Microsoft.Framework.FileSystemGlobbing
{
    public static class MatcherExtensions
    {
        public static void AddExcludePatterns(this Matcher matcher, params IEnumerable<string>[] excludePatternsGroups)
        {
            foreach (var group in excludePatternsGroups)
            {
                foreach (var pattern in group)
                {
                    matcher.AddExclude(pattern);
                }
            }
        }

        public static void AddIncludePatterns(this Matcher matcher, params IEnumerable<string>[] includePatternsGroups)
        {
            foreach (var group in includePatternsGroups)
            {
                foreach (var pattern in group)
                {
                    matcher.AddInclude(pattern);
                }
            }
        }

        public static IEnumerable<string> GetResultsInFullPath(this Matcher matcher, string directoryPath)
        {
            var relativePaths = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(directoryPath))).Files;
            var result = relativePaths.Select(path => Path.GetFullPath(Path.Combine(directoryPath, path))).ToArray();

            return result;
        }
    }
}
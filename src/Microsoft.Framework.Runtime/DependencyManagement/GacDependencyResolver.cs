// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using NuGet;

namespace Microsoft.Framework.Runtime
{
    public class GacDependencyResolver : IDependencyProvider, ILibraryExportProvider
    {
        private readonly Dictionary<string, string> _resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> GetAttemptedPaths(FrameworkName targetFramework)
        {
            if (PlatformHelper.IsMono)
            {
                return Enumerable.Empty<string>();
            }

            if (!VersionUtility.IsDesktop(targetFramework))
            {
                return Enumerable.Empty<string>();
            }

            return GetGacSearchPaths().Select(p => Path.Combine(p, "{name}", "{version}", "{name}.dll"));
        }

        public LibraryDescription GetDescription(string name, SemanticVersion version, FrameworkName targetFramework)
        {
            if (PlatformHelper.IsMono)
            {
                return null;
            }

            if (!VersionUtility.IsDesktop(targetFramework))
            {
                return null;
            }

            string path;
            if (!TryResolvePartialName(name, out path))
            {
                return null;
            }

            SemanticVersion assemblyVersion = VersionUtility.GetAssemblyVersion(path);

            if (version == null || version == assemblyVersion)
            {
                _resolvedPaths[name] = path;

                return new LibraryDescription
                {
                    Identity = new Library { Name = name, Version = assemblyVersion },
                    Dependencies = Enumerable.Empty<Library>()
                };
            }

            return null;
        }

        public void Initialize(IEnumerable<LibraryDescription> dependencies, FrameworkName targetFramework)
        {
            foreach (var d in dependencies)
            {
                d.Path = _resolvedPaths[d.Identity.Name];
                d.Type = "Assembly";
            }
        }

        public ILibraryExport GetLibraryExport(string name, FrameworkName targetFramework, string configuration)
        {
            string assemblyPath;
            if (_resolvedPaths.TryGetValue(name, out assemblyPath))
            {
                return new LibraryExport(name, assemblyPath);
            }

            return null;
        }

        private bool TryResolvePartialName(string name, out string assemblyLocation)
        {
            foreach (var gacPath in GetGacSearchPaths())
            {
                var di = new DirectoryInfo(Path.Combine(gacPath, name));

                if (!di.Exists)
                {
                    continue;
                }

                var match = di.EnumerateFiles("*.dll", SearchOption.AllDirectories)
                                .FirstOrDefault(d => Path.GetFileNameWithoutExtension(d.Name).Equals(name, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    assemblyLocation = match.FullName;
                    return true;
                }
            }

            assemblyLocation = null;
            return false;
        }

        private static IEnumerable<string> GetGacSearchPaths()
        {
            var gacFolders = new[] { IntPtr.Size == 4 ? "GAC_32" : "GAC_64", "GAC_MSIL" };
            string windowsFolder = Environment.GetEnvironmentVariable("WINDIR");

            foreach (var folder in gacFolders)
            {
                yield return Path.Combine(windowsFolder,
                                          "Microsoft.NET",
                                          "assembly",
                                          folder);
            }
        }
    }
}
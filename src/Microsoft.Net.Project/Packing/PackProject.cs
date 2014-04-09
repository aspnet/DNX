﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Net.Runtime;
using System.IO.Compression;

namespace Microsoft.Net.Project.Packing
{
    public class PackProject
    {
        private readonly ProjectReferenceDependencyProvider _projectReferenceDependencyProvider;
        private readonly IProjectResolver _projectResolver;
        private readonly LibraryDescription _libraryDescription;

        public PackProject(
            ProjectReferenceDependencyProvider projectReferenceDependencyProvider,
            IProjectResolver projectResolver,
            LibraryDescription libraryDescription)
        {
            _projectReferenceDependencyProvider = projectReferenceDependencyProvider;
            _projectResolver = projectResolver;
            _libraryDescription = libraryDescription;
        }

        public string Name { get { return _libraryDescription.Identity.Name; } }
        public string TargetPath { get; private set; }

        public void Emit(PackRoot root)
        {
            Console.WriteLine("Packing project dependency {0}", _libraryDescription.Identity.Name);

            Runtime.Project project;
            if (!_projectResolver.TryResolveProject(_libraryDescription.Identity.Name, out project))
            {
                throw new Exception("TODO: unable to resolve project named " + _libraryDescription.Identity.Name);
            }

            var targetName = project.Name;
            TargetPath = Path.Combine(root.OutputPath, targetName);

            Console.WriteLine("  Source {0}", project.ProjectDirectory);
            Console.WriteLine("  Target {0}", TargetPath);

            root.Operations.Delete(TargetPath);
            root.Operations.Copy(project.ProjectDirectory, TargetPath, IsProjectFileIncluded);
        }

        private static bool IsProjectFileIncluded(bool isRoot, string fileName)
        {
            var fileExtension = Path.GetExtension(fileName);

            if (!isRoot)
            {
                return true;
            }

            if (string.Equals(fileExtension, ".csproj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileExtension, ".kproj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileExtension, ".user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "obj", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        public void PostProcess(PackRoot root)
        {
            var binFolderPath = Path.Combine(TargetPath, "bin");

            // Copy tools/*.dll into bin to support AspNet.Loader.dll
            foreach (var package in root.Packages)
            {
                var packageToolsPath = Path.Combine(package.TargetPath, "tools");
                if (Directory.Exists(packageToolsPath))
                {
                    foreach (var packageToolFile in Directory.EnumerateFiles(packageToolsPath, "*.dll").Select(Path.GetFileName))
                    {
                        if (!Directory.Exists(binFolderPath))
                        {
                            Directory.CreateDirectory(binFolderPath);
                        }

                        File.Copy(
                            Path.Combine(packageToolsPath, packageToolFile),
                            Path.Combine(binFolderPath, packageToolFile),
                            true);
                    }
                }
            }

            // Copy lib/**/*.dll into bin/bundle-{guid}.zip to support web deploy
            if (root.Bundle)
            {
                if (!Directory.Exists(binFolderPath))
                {
                    Directory.CreateDirectory(binFolderPath);
                }

                var targetFile = Path.Combine(binFolderPath, "bundle-" + Guid.NewGuid().ToString("n") + ".zip");

                using (var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var archive = new ZipArchive(targetStream, ZipArchiveMode.Create))
                    {
                        foreach (var package in root.Packages)
                        {
                            root.Operations.AddFiles(
                                archive,
                                package.TargetPath,
                                Path.Combine("packages", package.Library.Name + "." + package.Library.Version),
                                IncludePackageFileInBundle);
                        }
                    }
                }
            }
        }

        private bool IncludePackageFileInBundle(string relativePath, string fileName)
        {
            var fileExtension = Path.GetExtension(fileName);
            var rootFolder = BasePath(relativePath);

            if (string.Equals(rootFolder, "lib", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fileExtension, ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.IsNullOrEmpty(relativePath) &&
                (string.Equals(fileExtension, ".sha512", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileExtension, ".nuspec", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return false;
        }

        private string BasePath(string relativePath)
        {
            var index1 = (relativePath + Path.DirectorySeparatorChar).IndexOf(Path.DirectorySeparatorChar);
            var index2 = (relativePath + Path.AltDirectorySeparatorChar).IndexOf(Path.AltDirectorySeparatorChar);
            return relativePath.Substring(0, Math.Min(index1, index2));
        }
    }
}

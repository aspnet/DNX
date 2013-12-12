﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Net.Runtime;
using Microsoft.Net.Runtime.FileSystem;
using Microsoft.Net.Runtime.Loader;
using Microsoft.Net.Runtime.Loader.MSBuildProject;
using Microsoft.Net.Runtime.Loader.NuGet;
using Microsoft.Net.Runtime.Loader.Roslyn;
using NuGet;
using KProject = Microsoft.Net.Runtime.Loader.Project;

namespace Microsoft.Net.Project
{
    public class ProjectManager
    {
        private readonly string _projectDir;

        public ProjectManager(string projectDir)
        {
            _projectDir = Normalize(projectDir);
        }

        public void Build(string defaultTargetFramework = "net45")
        {
            KProject project;
            if (!KProject.TryGetProject(_projectDir, out project))
            {
                Trace.TraceInformation("Unable to locate {0}.'", KProject.ProjectFileName);
                return;
            }

            var sw = Stopwatch.StartNew();

            string outputPath = Path.Combine(_projectDir, "bin");
            string nupkg = GetPackagePath(project, outputPath);

            var configurations = new HashSet<FrameworkName>(
                project.GetTargetFrameworkConfigurations()
                       .Select(c => c.FrameworkName));

            if (configurations.Count == 0)
            {
                configurations.Add(VersionUtility.ParseFrameworkName(defaultTargetFramework));
            }

            var builder = new PackageBuilder();

            // TODO: Support nuspecs in the project folder
            builder.Authors.AddRange(project.Authors);

            if (builder.Authors.Count == 0)
            {
                // Temporary
                builder.Authors.Add("K");
            }

            builder.Description = project.Description ?? project.Name;
            builder.Id = project.Name;
            builder.Version = project.Version;
            builder.Title = project.Name;

            bool createPackage = false;

            // Build all target frameworks a project supports
            foreach (var targetFramework in configurations)
            {
                try
                {
                    var result = Build(project, outputPath, targetFramework, builder);

                    if (result != null && result.Errors != null)
                    {
                        Trace.TraceError(String.Join(Environment.NewLine, result.Errors));
                    }
                    else
                    {
                        createPackage = true;
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }
            }

            if (createPackage)
            {
                using (var fs = File.Create(nupkg))
                {
                    builder.Save(fs);
                }

                Trace.TraceInformation("{0} -> {1}", project.Name, nupkg);
            }

            sw.Stop();

            Trace.TraceInformation("Compile took {0}ms", sw.ElapsedMilliseconds);
        }

        public void Clean(string defaultTargetFramework = "net45")
        {
            KProject project;
            if (!KProject.TryGetProject(_projectDir, out project))
            {
                Trace.TraceInformation("Unable to locate {0}.'", KProject.ProjectFileName);
                return;
            }

            string outputPath = Path.Combine(_projectDir, "bin");
            string nupkg = GetPackagePath(project, outputPath);

            var configurations = new HashSet<FrameworkName>(
                project.GetTargetFrameworkConfigurations()
                       .Select(c => c.FrameworkName));

            if (configurations.Count == 0)
            {
                configurations.Add(VersionUtility.ParseFrameworkName(defaultTargetFramework));
            }

            foreach (var targetFramework in configurations)
            {
                try
                {
                    var result = Clean(project, outputPath, targetFramework);

                    if (result != null && result.Errors != null)
                    {
                        Trace.TraceError(String.Join(Environment.NewLine, result.Errors));
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }
            }

            if (File.Exists(nupkg))
            {
                Trace.TraceInformation("Cleaning {0}", nupkg);
                File.Delete(nupkg);
            }

            var di = new DirectoryInfo(outputPath);
            DeleteEmptyFolders(di);
        }

        private static void DeleteEmptyFolders(DirectoryInfo di)
        {
            if (!di.Exists)
            {
                return;
            }

            foreach (var d in di.EnumerateDirectories())
            {
                DeleteEmptyFolders(d);
            }

            if (!di.EnumerateFileSystemInfos().Any())
            {
                di.Delete();
            }
        }
 
        private static AssemblyLoadResult Build(KProject project, string outputPath, FrameworkName targetFramework, PackageBuilder builder)
        {
            var loader = CreateLoader(Path.GetDirectoryName(project.ProjectFilePath));

            loader.Walk(project.Name, project.Version, targetFramework);

            var targetFrameworkFolder = VersionUtility.GetShortFrameworkName(targetFramework);
            string targetPath = Path.Combine(outputPath, targetFrameworkFolder);

            var loadContext = new LoadContext(project.Name, targetFramework)
            {
                OutputPath = targetPath,
                PackageBuilder = builder,
            };

            var result = loader.Load(loadContext);

            if (result == null || result.Errors != null)
            {
                return result;
            }

            // REVIEW: This might not work so well when building for multiple frameworks
            RunStaticMethod("Compiler", "Compile", targetPath);

            return result;
        }

        private static AssemblyLoadResult Clean(KProject project, string outputPath, FrameworkName targetFramework)
        {
            var loader = CreateLoader(Path.GetDirectoryName(project.ProjectFilePath));
            loader.Walk(project.Name, project.Version, targetFramework);

            var targetFrameworkFolder = VersionUtility.GetShortFrameworkName(targetFramework);
            string targetPath = Path.Combine(outputPath, targetFrameworkFolder);

            var loadContext = new LoadContext(project.Name, targetFramework)
            {
                OutputPath = targetPath,
                ArtifactPaths = new List<string>(),
                CreateArtifacts = false
            };

            var result = loader.Load(loadContext);

            if (result == null || result.Errors != null)
            {
                return result;
            }

            // REVIEW: This might not work so well when building for multiple frameworks
            RunStaticMethod("Compiler", "Clean", targetPath, loadContext.ArtifactPaths);

            if (loadContext.ArtifactPaths.Count > 0)
            {
                Trace.TraceInformation("Cleaning generated artifacts for {0}", targetFramework);

                foreach (var path in loadContext.ArtifactPaths)
                {
                    if (File.Exists(path))
                    {
                        Trace.TraceInformation("Cleaning {0}", path);

                        File.Delete(path);
                    }
                }
            }

            return result;
        }

        private static void RunStaticMethod(string typeName, string methodName, params object[] args)
        {
            // Invoke a static method on a class with the specified args
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = a.GetType(typeName);

                if (type != null)
                {
                    Trace.TraceInformation("Found {0} in {1}", typeName, a.GetName().Name);

                    var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                    if (method != null)
                    {
                        method.Invoke(null, args);
                    }
                }
            }
        }
        private static AssemblyLoader CreateLoader(string projectDir)
        {
            var loader = new AssemblyLoader();
            string rootDirectory = ResolveRootDirectory(projectDir);
            var resolver = new FrameworkReferenceResolver();
            var resourceProvider = new ResxResourceProvider();
            var roslynLoader = new RoslynAssemblyLoader(rootDirectory, NoopWatcher.Instance, resolver, loader, resourceProvider);
            loader.Add(roslynLoader);
            loader.Add(new MSBuildProjectAssemblyLoader(rootDirectory, NoopWatcher.Instance));
            loader.Add(new NuGetAssemblyLoader(projectDir));

            return loader;
        }

        private static string ResolveRootDirectory(string projectDir)
        {
            var di = new DirectoryInfo(projectDir);

            if (di.Parent != null)
            {
                if (di.EnumerateFiles("*.sln").Any() ||
                   di.EnumerateDirectories("packages").Any() ||
                   di.EnumerateDirectories(".git").Any())
                {
                    return di.FullName;
                }

                di = di.Parent;
            }

            return Path.GetDirectoryName(projectDir);
        }

        private static string Normalize(string projectDir)
        {
            if (File.Exists(projectDir))
            {
                projectDir = Path.GetDirectoryName(projectDir);
            }

            return Path.GetFullPath(projectDir.TrimEnd(Path.DirectorySeparatorChar));
        }

        private static string GetPackagePath(KProject project, string outputPath)
        {
            return Path.Combine(outputPath, project.Name + "." + project.Version + ".nupkg");
        }
    }
}

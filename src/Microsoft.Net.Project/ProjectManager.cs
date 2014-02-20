﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Net.Runtime;
using Microsoft.Net.Runtime.FileSystem;
using Microsoft.Net.Runtime.Loader;
using Microsoft.Net.Runtime.Loader.MSBuildProject;
using Microsoft.Net.Runtime.Loader.NuGet;
using Microsoft.Net.Runtime.Loader.Roslyn;
using Microsoft.Net.Runtime.Roslyn;
using NuGet;
using KProject = Microsoft.Net.Runtime.Project;

namespace Microsoft.Net.Project
{
    public class ProjectManager
    {
        private readonly BuildOptions _buildOptions;

        public ProjectManager(BuildOptions buildOptions)
        {
            _buildOptions = buildOptions;
            _buildOptions.ProjectDir = Normalize(buildOptions.ProjectDir);
        }

        public bool Build()
        {
            KProject project;
            if (!KProject.TryGetProject(_buildOptions.ProjectDir, out project))
            {
                Trace.TraceInformation("Unable to locate {0}.'", KProject.ProjectFileName);
                return false;
            }

            var sw = Stopwatch.StartNew();

            string outputPath = _buildOptions.OutputDir ?? Path.Combine(_buildOptions.ProjectDir, "bin");
            string nupkg = GetPackagePath(project, outputPath);

            var configurations = new HashSet<FrameworkName>(
                project.GetTargetFrameworkConfigurations()
                       .Select(c => c.FrameworkName));

            if (configurations.Count == 0)
            {
                configurations.Add(VersionUtility.ParseFrameworkName(_buildOptions.RuntimeTargetFramework));
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

            bool success = true;
            bool createPackage = false;

            var allErrors = new List<string>();

            // Build all target frameworks a project supports
            foreach (var targetFramework in configurations)
            {
                try
                {
                    var errors = new List<string>();

                    success = success && Build(project, outputPath, targetFramework, builder, errors);

                    allErrors.AddRange(errors);

                    if (errors.Count > 0)
                    {
                        Trace.TraceError(String.Join(Environment.NewLine, errors));
                    }
                    else
                    {
                        createPackage = true;
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    Trace.TraceError(ex.ToString());
                }
            }

            foreach (var sharedFile in project.SharedFiles)
            {
                var file = new PhysicalPackageFile();
                file.SourcePath = sharedFile;
                file.TargetPath = String.Format(@"shared\{0}", Path.GetFileName(sharedFile));
                builder.Files.Add(file);
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
            return success;
        }

        private bool Build(KProject project, string outputPath, FrameworkName targetFramework, PackageBuilder builder, List<string> errors)
        {
            var roslynLoader = PrepareCompiler(project, targetFramework);

            var targetFrameworkFolder = VersionUtility.GetShortFrameworkName(targetFramework);
            string targetPath = Path.Combine(outputPath, targetFrameworkFolder);

            var buildContext = new BuildContext(project.Name, targetFramework)
            {
                OutputPath = targetPath,
                PackageBuilder = builder,
                CopyDependencies = _buildOptions.CopyDependencies
            };

            return roslynLoader.Build(buildContext, errors);
        }

        private static RoslynAssemblyLoader PrepareCompiler(KProject project, FrameworkName frameworkName)
        {
            string projectDir = project.ProjectDirectory;

            var globalAssemblyCache = new DefaultGlobalAssemblyCache();

            var loader = new AssemblyLoader();
            string rootDirectory = DefaultHost.ResolveRootDirectory(projectDir);
            var resolver = new FrameworkReferenceResolver(globalAssemblyCache);
            var resourceProvider = new ResxResourceProvider();
            var projectResolver = new ProjectResolver(projectDir, rootDirectory);
            var roslynLoader = new RoslynAssemblyLoader(projectResolver, NoopWatcher.Instance, resolver, globalAssemblyCache, loader, resourceProvider);
            loader.Add(roslynLoader);
            loader.Add(new MSBuildProjectAssemblyLoader(rootDirectory, NoopWatcher.Instance));
            loader.Add(new NuGetAssemblyLoader(projectDir));

            loader.Walk(project.Name, project.Version, frameworkName);

            return roslynLoader;
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

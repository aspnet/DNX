// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Microsoft.Framework.PackageManager.Packing
{
    public class PackManager
    {
        private readonly IServiceProvider _hostServices;
        private readonly PackOptions _options;

        public PackManager(IServiceProvider hostServices, PackOptions options)
        {
            _hostServices = hostServices;
            _options = options;
            _options.ProjectDir = Normalize(_options.ProjectDir);

            var outputDir = _options.OutputDir ?? Path.Combine(_options.ProjectDir, "bin", "output");
            _options.OutputDir = Normalize(outputDir);
            ScriptExecutor = new ScriptExecutor();
        }

        public ScriptExecutor ScriptExecutor { get; private set; }

        private static string Normalize(string projectDir)
        {
            if (File.Exists(projectDir))
            {
                projectDir = Path.GetDirectoryName(projectDir);
            }

            return Path.GetFullPath(projectDir.TrimEnd(Path.DirectorySeparatorChar));
        }

        public bool Package()
        {
            Runtime.Project project;
            if (!Runtime.Project.TryGetProject(_options.ProjectDir, out project))
            {
                _options.Reports.Information.WriteLine("Unable to locate {0}.'".Red(), Runtime.Project.ProjectFileName);
                return false;
            }

            // '--wwwroot' option can override 'webroot' property in project.json
            _options.WwwRoot = _options.WwwRoot ?? project.WebRoot;
            _options.WwwRootOut = _options.WwwRootOut ?? _options.WwwRoot;

            if (string.IsNullOrEmpty(_options.WwwRoot) && !string.IsNullOrEmpty(_options.WwwRootOut))
            {
                _options.Reports.Information.WriteLine(
                    "'--wwwroot-out' option can be used only when the '--wwwroot' option or 'webroot' in project.json is specified.".Red());
                return false;
            }

            if (!string.IsNullOrEmpty(_options.WwwRoot) &&
                !Directory.Exists(Path.Combine(project.ProjectDirectory, _options.WwwRoot)))
            {
                _options.Reports.Information.WriteLine(
                    "The specified wwwroot folder '{0}' doesn't exist in the project directory.".Red(), _options.WwwRoot);
                return false;
            }

            if (string.Equals(_options.WwwRootOut, PackRoot.AppRootName, StringComparison.OrdinalIgnoreCase))
            {
                _options.Reports.Information.WriteLine(
                    "'{0}' is a reserved folder name. Please choose another name for the wwwroot-out folder.".Red(),
                    PackRoot.AppRootName);
                return false;
            }

            var sw = Stopwatch.StartNew();

            string outputPath = _options.OutputDir;

            var projectDir = project.ProjectDirectory;

            var frameworkContexts = new Dictionary<FrameworkName, DependencyContext>();

            var root = new PackRoot(project, outputPath, _hostServices, _options.Reports)
            {
                Overwrite = _options.Overwrite,
                Configuration = _options.Configuration,
                NoSource = _options.NoSource
            };

            Func<string, string> getVariable = key =>
            {
                return null;
            };

            ScriptExecutor.Execute(project, "prepare", getVariable);

            ScriptExecutor.Execute(project, "prepack", getVariable);

            foreach (var runtime in _options.Runtimes)
            {
                var frameworkName = DependencyContext.GetFrameworkNameForRuntime(Path.GetFileName(runtime));
                var runtimeLocated = TryAddRuntime(root, frameworkName, runtime);

                if (!runtimeLocated)
                {
                    var kreHome = Environment.GetEnvironmentVariable("KRE_HOME");
                    if (string.IsNullOrEmpty(kreHome))
                    {
                        kreHome = Environment.GetEnvironmentVariable("ProgramFiles") + @"\KRE;%USERPROFILE%\.kre";
                    }

                    foreach (var portion in kreHome.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var packagesPath = Path.Combine(
                            Environment.ExpandEnvironmentVariables(portion),
                            "packages",
                            runtime);

                        if (TryAddRuntime(root, frameworkName, packagesPath))
                        {
                            runtimeLocated = true;
                            break;
                        }
                    }
                }

                if (!runtimeLocated)
                {
                    _options.Reports.Information.WriteLine(string.Format("Unable to locate runtime '{0}'", runtime.Red().Bold()));
                    return false;
                }

                if (!project.GetTargetFrameworks().Any(x => x.FrameworkName == frameworkName))
                {
                    _options.Reports.Information.WriteLine(
                        string.Format("'{0}' is not a target framework of the project being packed",
                        frameworkName.ToString().Red().Bold()));
                    return false;
                }

                if (!frameworkContexts.ContainsKey(frameworkName))
                {
                    frameworkContexts[frameworkName] = CreateDependencyContext(project, frameworkName);
                }
            }

            // If there is no target framework filter specified with '--runtime',
            // the packed output targets all frameworks specified in project.json
            if (!_options.Runtimes.Any())
            {
                foreach (var frameworkInfo in project.GetTargetFrameworks())
                {
                    if (!frameworkContexts.ContainsKey(frameworkInfo.FrameworkName))
                    {
                        frameworkContexts[frameworkInfo.FrameworkName] =
                            CreateDependencyContext(project, frameworkInfo.FrameworkName);
                    }
                }
            }

            if (!frameworkContexts.Any())
            {
                var frameworkName = DependencyContext.GetFrameworkNameForRuntime("KRE-CLR-x86.*");
                frameworkContexts[frameworkName] = CreateDependencyContext(project, frameworkName);
            }

            root.SourcePackagesPath = frameworkContexts.First().Value.PackagesDirectory;

            bool anyUnresolvedDependency = false;
            foreach (var dependencyContext in frameworkContexts.Values)
            {
                // If there's any unresolved dependencies then complain and keep working
                if (dependencyContext.UnresolvedDependencyProvider.UnresolvedDependencies.Any())
                {
                    anyUnresolvedDependency = true;
                    var message = "Warning: " +
                        dependencyContext.UnresolvedDependencyProvider.GetMissingDependenciesWarning(
                            dependencyContext.FrameworkName);
                    _options.Reports.Quiet.WriteLine(message.Yellow());
                }

                foreach (var libraryDescription in dependencyContext.NuGetDependencyResolver.Dependencies)
                {
                    IList<DependencyContext> contexts;
                    if (!root.LibraryDependencyContexts.TryGetValue(libraryDescription.Identity, out contexts))
                    {
                        root.Packages.Add(new PackPackage(libraryDescription));
                        contexts = new List<DependencyContext>();
                        root.LibraryDependencyContexts[libraryDescription.Identity] = contexts;
                    }
                    contexts.Add(dependencyContext);
                }

                foreach (var libraryDescription in dependencyContext.ProjectReferenceDependencyProvider.Dependencies)
                {
                    if (!root.Projects.Any(p => p.Name == libraryDescription.Identity.Name))
                    {
                        var packProject = new PackProject(
                            dependencyContext.ProjectReferenceDependencyProvider,
                            dependencyContext.ProjectResolver,
                            libraryDescription);

                        if (packProject.Name == project.Name)
                        {
                            packProject.WwwRoot = _options.WwwRoot;
                            packProject.WwwRootOut = _options.WwwRootOut;
                        }
                        root.Projects.Add(packProject);
                    }
                }
            }

            NativeImageGenerator nativeImageGenerator = null;
            if (_options.Native)
            {
                nativeImageGenerator = NativeImageGenerator.Create(_options, root, frameworkContexts.Values);
                if (nativeImageGenerator == null)
                {
                    _options.Reports.Information.WriteLine("Fail to initiate native image generation process.".Red());
                    return false;
                }
            }

            root.Emit();

            ScriptExecutor.Execute(project, "postpack", getVariable);

            if (_options.Native && !nativeImageGenerator.BuildNativeImages(root))
            {
                _options.Reports.Information.WriteLine("Native image generation failed.");
                return false;
            }

            sw.Stop();

            _options.Reports.Information.WriteLine("Time elapsed {0}", sw.Elapsed);
            return !anyUnresolvedDependency;
        }

        bool TryAddRuntime(PackRoot root, FrameworkName frameworkName, string krePath)
        {
            if (!Directory.Exists(krePath))
            {
                return false;
            }

            var kreName = Path.GetFileName(Path.GetDirectoryName(Path.Combine(krePath, ".")));
            var kreNupkgPath = Path.Combine(krePath, kreName + ".nupkg");
            if (!File.Exists(kreNupkgPath))
            {
                return false;
            }

            root.Runtimes.Add(new PackRuntime(root, frameworkName, kreNupkgPath));
            return true;
        }

        private DependencyContext CreateDependencyContext(Runtime.Project project, FrameworkName frameworkName)
        {
            var dependencyContext = new DependencyContext(project.ProjectDirectory, _options.Configuration, frameworkName);
            dependencyContext.Walk(project.Name, project.Version);
            return dependencyContext;
        }
    }
}
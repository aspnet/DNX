﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Net.Runtime.Common.DependencyInjection;
using Microsoft.Net.Runtime.FileSystem;
using Microsoft.Net.Runtime.Loader;
using Microsoft.Net.Runtime.Loader.NuGet;

namespace Microsoft.Net.Runtime
{
    public class DefaultHost : IHost
    {
        private AssemblyLoader _loader;
        private DependencyWalker _dependencyWalker;

        private IFileWatcher _watcher;
        private readonly string _projectDir;
        private readonly FrameworkName _targetFramework;
        private readonly string _name;
        private readonly ServiceProvider _serviceProvider;
        private readonly IAssemblyLoaderEngine _loaderEngine;
        private readonly ILibraryExportProvider _hostExporter;

        private Project _project;

        public DefaultHost(DefaultHostOptions options, IServiceProvider hostProvider)
        {
            _projectDir = Normalize(options.ApplicationBaseDirectory);

            _name = options.ApplicationName;

            _targetFramework = options.TargetFramework;

            _loaderEngine = (IAssemblyLoaderEngine)hostProvider.GetService(typeof(IAssemblyLoaderEngine));
            _hostExporter = (ILibraryExportProvider)hostProvider.GetService(typeof(ILibraryExportProvider));

            _serviceProvider = new ServiceProvider(hostProvider);

            Initialize(options);
        }

        public IServiceProvider ServiceProvider
        {
            get { return _serviceProvider; }
        }

        public Project Project
        {
            get { return _project; }
        }

        public Assembly GetEntryPoint(string applicationName)
        {
            Trace.TraceInformation("Project root is {0}", _projectDir);

            var sw = Stopwatch.StartNew();

            if (Project == null)
            {
                return null;
            }

            _dependencyWalker.Walk(Project.Name, Project.Version, _targetFramework);

            _serviceProvider.Add(typeof(IApplicationEnvironment), new ApplicationEnvironment(Project, _targetFramework));

            Trace.TraceInformation("Loading entry point from {0}", applicationName);

            var assembly = Assembly.Load(new AssemblyName(applicationName));

            sw.Stop();

            Trace.TraceInformation("Load took {0}ms", sw.ElapsedMilliseconds);

            return assembly;
        }

        public Assembly Load(string name)
        {
            return _loader.LoadAssembly(new LoadContext(name, _targetFramework));
        }

        public void Dispose()
        {
            _watcher.Dispose();
        }

        private void Initialize(DefaultHostOptions options)
        {
            var dependencyProviders = new List<IDependencyProvider>();
            var loaders = new List<IAssemblyLoader>();

            string rootDirectory = ResolveRootDirectory(_projectDir);

            if (options.WatchFiles)
            {
                _watcher = new FileWatcher(rootDirectory);
            }
            else
            {
                _watcher = NoopWatcher.Instance;
            }

            if (!Project.TryGetProject(_projectDir, out _project))
            {
                Trace.TraceInformation("Unable to locate {0}.'", Project.ProjectFileName);
                return;
            }

            var projectResolver = new ProjectResolver(_projectDir, rootDirectory);

            var nugetDependencyResolver = new NuGetDependencyResolver(_projectDir, options.PackageDirectory);
            var nugetLoader = new NuGetAssemblyLoader(_loaderEngine, nugetDependencyResolver);
            var globalAssemblyCache = new DefaultGlobalAssemblyCache();

            // Roslyn needs to be able to resolve exported references and sources
            var libraryExporters = new List<ILibraryExportProvider>();

            // Add the host exporter
            libraryExporters.Add(_hostExporter);

            // GAC
            libraryExporters.Add(new GacLibraryExportProvider(globalAssemblyCache));

            // NuGet exporter
            libraryExporters.Add(nugetDependencyResolver);

            var dependencyExporter = new CompositeLibraryExportProvider(libraryExporters);
            var roslynLoader = new LazyRoslynAssemblyLoader(_loaderEngine, projectResolver, _watcher, dependencyExporter, globalAssemblyCache);

            // Project.json projects
            loaders.Add(roslynLoader);
            dependencyProviders.Add(new ProjectReferenceDependencyProvider(projectResolver));

            // NuGet packages
            loaders.Add(nugetLoader);
            dependencyProviders.Add(nugetDependencyResolver);

            _dependencyWalker = new DependencyWalker(dependencyProviders);
            _loader = new AssemblyLoader(loaders);
            _serviceProvider.Add(typeof(IFileMonitor), _watcher);
            _serviceProvider.Add(typeof(ILibraryManager),
                new LibraryManager(_targetFramework, 
                                   _dependencyWalker, 
                                   libraryExporters.Concat(new[] { roslynLoader })));
        }

        public static string ResolveRootDirectory(string projectDir)
        {
            var di = new DirectoryInfo(Path.GetDirectoryName(projectDir));

            while (di.Parent != null)
            {
                if (di.EnumerateFiles("*." + GlobalSettings.GlobalFileName).Any() ||
                    di.EnumerateFiles("*.sln").Any() ||
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
    }
}

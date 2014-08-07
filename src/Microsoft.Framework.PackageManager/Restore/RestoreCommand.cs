// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
#if NET45
using System.IO.Packaging;
#endif
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Framework.PackageManager.Packing;
using Microsoft.Framework.PackageManager.Restore.NuGet;
using Microsoft.Framework.Runtime;
using Newtonsoft.Json.Linq;
using NuGet;
using NuGet.Common;

namespace Microsoft.Framework.PackageManager
{
    public class RestoreCommand
    {
        public RestoreCommand(IApplicationEnvironment env)
        {
            ApplicationEnvironment = env;
            FileSystem = new PhysicalFileSystem(Directory.GetCurrentDirectory());
            MachineWideSettings = new CommandLineMachineWideSettings();
            Sources = Enumerable.Empty<string>();
            FallbackSources = Enumerable.Empty<string>();
            ScriptExecutor = new ScriptExecutor();
        }

        public string RestoreDirectory { get; set; }
        public string NuGetConfigFile { get; set; }
        public IEnumerable<string> Sources { get; set; }
        public IEnumerable<string> FallbackSources { get; set; }
        public bool NoCache { get; set; }
        public string PackageFolder { get; set; }
        public string GlobalJsonFile { get; set; }

        public ScriptExecutor ScriptExecutor { get; private set; }

        public IApplicationEnvironment ApplicationEnvironment { get; private set; }
        public IMachineWideSettings MachineWideSettings { get; set; }
        public IFileSystem FileSystem { get; set; }
        public Reports Reports { get; set; }

        protected internal ISettings Settings { get; set; }
        protected internal IPackageSourceProvider SourceProvider { get; set; }

        public bool ExecuteCommand()
        {
            var sw = new Stopwatch();
            sw.Start();

            var restoreDirectory = RestoreDirectory ?? Directory.GetCurrentDirectory();

            var projectJsonFiles = Directory.GetFiles(restoreDirectory, "project.json", SearchOption.AllDirectories);

            var rootDirectory = ProjectResolver.ResolveRootDirectory(restoreDirectory);
            ReadSettings(rootDirectory);

            string packagesDirectory = PackageFolder;

            if (string.IsNullOrEmpty(PackageFolder))
            {
                packagesDirectory = NuGetDependencyResolver.ResolveRepositoryPath(rootDirectory);
            }

            var packagesFolderFileSystem = CreateFileSystem(packagesDirectory);
            var pathResolver = new DefaultPackagePathResolver(packagesFolderFileSystem, useSideBySidePaths: true);
            var localRepository = new LocalPackageRepository(pathResolver, packagesFolderFileSystem);

            int restoreCount = 0;
            int successCount = 0;
            foreach (var projectJsonPath in projectJsonFiles)
            {
                restoreCount += 1;
                var success = RestoreForProject(localRepository, projectJsonPath, rootDirectory, packagesDirectory).Result;
                if (success)
                {
                    successCount += 1;
                }
            }

            if (restoreCount > 1)
            {
                Reports.Information.WriteLine(string.Format("Total time {0}ms", sw.ElapsedMilliseconds));
            }

            return restoreCount == successCount;
        }

        private async Task<bool> RestoreForProject(LocalPackageRepository localRepository, string projectJsonPath, string rootDirectory, string packagesDirectory)
        {
            var success = true;

            Reports.Information.WriteLine(string.Format("Restoring packages for {0}", projectJsonPath.Bold()));

            var sw = new Stopwatch();
            sw.Start();

            Project project;
            if (!Project.TryGetProject(projectJsonPath, out project))
            {
                throw new Exception("TODO: project.json parse error");
            }

            Func<string, string> getVariable = key =>
            {
                return null;
            };

            ScriptExecutor.Execute(project, "prerestore", getVariable);

            var projectDirectory = project.ProjectDirectory;
            var restoreOperations = new RestoreOperations { Report = Reports.Information };
            var projectProviders = new List<IWalkProvider>();
            var localProviders = new List<IWalkProvider>();
            var remoteProviders = new List<IWalkProvider>();
            var contexts = new List<RestoreContext>();

            projectProviders.Add(
                new LocalWalkProvider(
                    new ProjectReferenceDependencyProvider(
                        new ProjectResolver(
                            projectDirectory,
                            rootDirectory))));

            localProviders.Add(
                new LocalWalkProvider(
                    new NuGetDependencyResolver(
                        packagesDirectory,
                        new EmptyFrameworkResolver())));

            var allSources = SourceProvider.LoadPackageSources();

            var enabledSources = Sources.Any() ?
                Enumerable.Empty<PackageSource>() :
                allSources.Where(s => s.IsEnabled);

            var addedSources = Sources.Concat(FallbackSources).Select(
                value => allSources.FirstOrDefault(source => CorrectName(value, source)) ?? new PackageSource(value));

            var effectiveSources = enabledSources.Concat(addedSources).Distinct().ToList();

            foreach (var source in effectiveSources)
            {
                if (new Uri(source.Source).IsFile)
                {
                    remoteProviders.Add(
                        new RemoteWalkProvider(
                            new PackageFolder(
                                source.Source,
                                Reports.Verbose)));
                }
                else
                {
                    remoteProviders.Add(
                        new RemoteWalkProvider(
                            new PackageFeed(
                                source.Source,
                                source.UserName,
                                source.Password,
                                NoCache,
                                Reports.Verbose)));
                }
            }

            foreach (var configuration in project.GetTargetFrameworks())
            {
                var context = new RestoreContext
                {
                    FrameworkName = configuration.FrameworkName,
                    ProjectLibraryProviders = projectProviders,
                    LocalLibraryProviders = localProviders,
                    RemoteLibraryProviders = remoteProviders,
                };
                contexts.Add(context);
            }
            if (!contexts.Any())
            {
                contexts.Add(new RestoreContext
                {
                    FrameworkName = ApplicationEnvironment.TargetFramework,
                    ProjectLibraryProviders = projectProviders,
                    LocalLibraryProviders = localProviders,
                    RemoteLibraryProviders = remoteProviders,
                });
            }

            var tasks = new List<Task<GraphNode>>();
            foreach (var context in contexts)
            {
                tasks.Add(restoreOperations.CreateGraphNode(context, new Library { Name = project.Name, Version = project.Version }, _ => true));
            }
            var graphs = await Task.WhenAll(tasks);

            Reports.Information.WriteLine(string.Format("{0}, {1}ms elapsed", "Resolving complete".Green(), sw.ElapsedMilliseconds));

            var installItems = new List<GraphItem>();
            var missingItems = new List<Library>();
            ForEach(graphs, node =>
            {
                if (node == null || node.Library == null)
                {
                    return;
                }
                if (node.Item == null || node.Item.Match == null)
                {
                    if (node.Library.Version != null && !missingItems.Contains(node.Library))
                    {
                        missingItems.Add(node.Library);
                        Reports.Information.WriteLine(string.Format("Unable to locate {0} >= {1}", node.Library.Name.Red().Bold(), node.Library.Version));
                        success = false;
                    }
                    return;
                }
                var isRemote = remoteProviders.Contains(node.Item.Match.Provider);
                var isAdded = installItems.Any(item => item.Match.Library == node.Item.Match.Library);
                if (!isAdded && isRemote)
                {
                    installItems.Add(node.Item);
                }
            });

            var dependencies = new Dictionary<Library, string>();

            // If there is a global.json file specified, we should do SHA value verification
            var globalJsonFileSpecified = !string.IsNullOrEmpty(GlobalJsonFile);
            JToken dependenciesNode = null;
            if (globalJsonFileSpecified)
            {
                var globalJson = JObject.Parse(File.ReadAllText(GlobalJsonFile));
                dependenciesNode = globalJson["dependencies"];
                if (dependenciesNode != null)
                {
                    dependencies = dependenciesNode
                        .OfType<JProperty>()
                        .ToDictionary(d => new Library()
                        {
                            Name = d.Name,
                            Version = SemanticVersion.Parse(d.Value.Value<string>("version"))
                        },
                        d => d.Value.Value<string>("sha"));
                }
            }

            var packagePathResolver = new DefaultPackagePathResolver(packagesDirectory);
            using (var sha512 = SHA512.Create())
            {
                foreach (var item in installItems)
                {
                    var library = item.Match.Library;

                    var memStream = new MemoryStream();
                    await item.Match.Provider.CopyToAsync(item.Match, memStream);
                    memStream.Seek(0, SeekOrigin.Begin);
                    var nupkgSHA = Convert.ToBase64String(sha512.ComputeHash(memStream));

                    string expectedSHA;
                    if (dependencies.TryGetValue(library, out expectedSHA))
                    {
                        if (!string.Equals(expectedSHA, nupkgSHA, StringComparison.Ordinal))
                        {
                            Reports.Information.WriteLine(
                                string.Format("SHA of downloaded package {0} doesn't match expected value.".Red().Bold(),
                                library.ToString()));
                            success = false;
                            continue;
                        }
                    }
                    else
                    {
                        // Report warnings only when given global.json contains "dependencies"
                        if (globalJsonFileSpecified && dependenciesNode != null)
                        {
                            Reports.Information.WriteLine(
                                string.Format("Expected SHA of package {0} doesn't exist in given global.json file.".Yellow().Bold(),
                                library.ToString()));
                        }
                    }

                    Reports.Information.WriteLine(string.Format("Installing {0} {1}", library.Name.Bold(), library.Version));

                    var targetPath = packagePathResolver.GetInstallPath(library.Name, library.Version);
                    var targetNupkg = packagePathResolver.GetPackageFilePath(library.Name, library.Version);
                    var hashPath = packagePathResolver.GetHashPath(library.Name, library.Version);

                    // Acquire the lock on a nukpg before we extract it to prevent the race condition when multiple
                    // processes are extracting to the same destination simultaneously
                    await ConcurrencyUtilities.ExecuteWithFileLocked(targetNupkg, async createdNewLock =>
                    {
                        // If this is the first process trying to install the target nupkg, go ahead
                        // After this process successfully installs the package, all other processes
                        // waiting on this lock don't need to install it again
                        if (createdNewLock)
                        {
                            Directory.CreateDirectory(targetPath);
                            using (var stream = new FileStream(targetNupkg, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                            {
                                await item.Match.Provider.CopyToAsync(item.Match, stream);
                                stream.Seek(0, SeekOrigin.Begin);

                                ExtractPackage(targetPath, stream);
                            }

                            File.WriteAllText(hashPath, nupkgSHA);
                        }

                        return 0;
                    });
                }
            }

            ScriptExecutor.Execute(project, "postrestore", getVariable);

            ScriptExecutor.Execute(project, "prepare", getVariable);

            Reports.Information.WriteLine(string.Format("{0}, {1}ms elapsed", "Restore complete".Green().Bold(), sw.ElapsedMilliseconds));

            // Print the dependency graph
            if (success)
            {
                var graphNum = contexts.Count;
                for (int i = 0; i < graphNum; i++)
                {
                    PrintDependencyGraph(graphs[i], contexts[i].FrameworkName);
                }
            }

            return success;
        }

        private void PrintDependencyGraph(GraphNode root, FrameworkName frameworkName)
        {
            // Box Drawing Unicode characters:
            // http://www.unicode.org/charts/PDF/U2500.pdf
            const char LIGHT_HORIZONTAL = '\u2500';
            const char LIGHT_UP_AND_RIGHT = '\u2514';
            const char LIGHT_VERTICAL_AND_RIGHT = '\u251C';

            var frameworkSuffix = string.Format(" [{0}]", frameworkName.ToString());
            Reports.Verbose.WriteLine(root.Item.Match.Library.ToString() + frameworkSuffix);

            Func<GraphNode, bool> isValidDependency = d => 
                (d != null && d.Library != null && d.Item != null && d.Item.Match != null);
            var dependencies = root.Dependencies.Where(isValidDependency).ToList();
            var dependencyNum = dependencies.Count;
            for (int i = 0; i < dependencyNum; i++)
            {
                var branchChar = LIGHT_VERTICAL_AND_RIGHT;
                if (i == dependencyNum - 1)
                {
                    branchChar = LIGHT_UP_AND_RIGHT;
                }

                var name = dependencies[i].Item.Match.Library.ToString();
                var dependencyListStr = string.Join(", ", dependencies[i].Dependencies
                    .Where(isValidDependency)
                    .Select(d => d.Item.Match.Library.ToString()));
                var format = string.IsNullOrEmpty(dependencyListStr) ? "{0}{1} {2}{3}" : "{0}{1} {2} ({3})";
                Reports.Verbose.WriteLine(string.Format(format,
                    branchChar, LIGHT_HORIZONTAL, name, dependencyListStr));
            }
            Reports.Verbose.WriteLine();
        }

        private static void ExtractPackage(string targetPath, FileStream stream)
        {
#if NET45
            if (PlatformHelper.IsMono)
            {
                using (var archive = Package.Open(stream, FileMode.Open, FileAccess.Read))
                {
                    var packOperations = new PackOperations();
                    packOperations.ExtractNupkg(archive, targetPath);
                }

                return;
            }
#endif

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var packOperations = new PackOperations();
                packOperations.ExtractNupkg(archive, targetPath);
            }
        }

        private bool CorrectName(string value, PackageSource source)
        {
            return source.Name.Equals(value, StringComparison.CurrentCultureIgnoreCase) ||
                source.Source.Equals(value, StringComparison.OrdinalIgnoreCase);
        }


        void ForEach(IEnumerable<GraphNode> nodes, Action<GraphNode> callback)
        {
            foreach (var node in nodes)
            {
                callback(node);
                ForEach(node.Dependencies, callback);
            }
        }

        void Display(string indent, IEnumerable<GraphNode> graphs)
        {
            foreach (var node in graphs)
            {
                Reports.Information.WriteLine(indent + node.Library.Name + "@" + node.Library.Version);
                Display(indent + " ", node.Dependencies);
            }
        }


        private void ReadSettings(string solutionDirectory)
        {
            // Read the solution-level settings
            var solutionSettingsFile = Path.Combine(
                solutionDirectory,
                NuGetConstants.NuGetSolutionSettingsFolder);
            var fileSystem = CreateFileSystem(solutionSettingsFile);

            if (NuGetConfigFile != null)
            {
                NuGetConfigFile = FileSystem.GetFullPath(NuGetConfigFile);
            }

            Settings = NuGet.Settings.LoadDefaultSettings(
                fileSystem: fileSystem,
                configFileName: NuGetConfigFile,
                machineWideSettings: MachineWideSettings);

            // Recreate the source provider and credential provider
            SourceProvider = PackageSourceBuilder.CreateSourceProvider(Settings);
            //HttpClient.DefaultCredentialProvider = new SettingsCredentialProvider(new ConsoleCredentialProvider(Console), SourceProvider, Console);

        }

        private IFileSystem CreateFileSystem(string path)
        {
            path = FileSystem.GetFullPath(path);
            return new PhysicalFileSystem(path);
        }

    }
}
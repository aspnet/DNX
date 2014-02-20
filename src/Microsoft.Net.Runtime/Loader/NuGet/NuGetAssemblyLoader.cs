﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using NuGet;

namespace Microsoft.Net.Runtime.Loader.NuGet
{
    public class NuGetAssemblyLoader : IPackageLoader, IDependencyExportResolver
    {
        private readonly LocalPackageRepository _repository;
        private readonly Dictionary<string, Assembly> _cache = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DependencyDescription> _dependencies = new Dictionary<string, DependencyDescription>();
        private readonly IDictionary<string, IList<string>> _sharedSources = new Dictionary<string, IList<string>>();

        public NuGetAssemblyLoader(string projectPath)
        {
            _repository = new LocalPackageRepository(ResolveRepositoryPath(projectPath));
        }

        public AssemblyLoadResult Load(LoadContext loadContext)
        {
            string name = loadContext.AssemblyName;

            Assembly assembly;
            if (_cache.TryGetValue(name, out assembly))
            {
                return new AssemblyLoadResult(assembly);
            }

            string path;
            if (_paths.TryGetValue(name, out path))
            {
                assembly = Assembly.LoadFile(path);

                _cache[name] = assembly;
            }

            if (assembly == null)
            {
                return null;
            }

            return new AssemblyLoadResult(assembly);
        }

        public DependencyDescription GetDescription(string name, SemanticVersion version, FrameworkName frameworkName)
        {
            var package = FindCandidate(name, version);

            if (package != null)
            {
                return new DependencyDescription
                {
                    Identity = new Dependency { Name = package.Id, Version = package.Version },
                    Dependencies = GetDependencies(package, frameworkName)
                };
            }

            return null;
        }

        private IEnumerable<Dependency> GetDependencies(IPackage package, FrameworkName targetFramework)
        {
            IEnumerable<PackageDependencySet> dependencySet;
            if (VersionUtility.TryGetCompatibleItems(targetFramework, package.DependencySets, out dependencySet))
            {
                foreach (var set in dependencySet)
                {
                    foreach (var d in set.Dependencies)
                    {
                        var dependency = _repository.FindPackagesById(d.Id)
                                                    .Where(d.VersionSpec.ToDelegate())
                                                    .FirstOrDefault();
                        if (dependency != null)
                        {
                            yield return new Dependency
                            {
                                Name = dependency.Id,
                                Version = dependency.Version
                            };
                        }
                    }
                }
            }
        }

        public void Initialize(IEnumerable<DependencyDescription> packages, FrameworkName targetFramework)
        {
            foreach (var dependency in packages)
            {
                _dependencies[dependency.Identity.Name] = dependency;

                var package = FindCandidate(dependency.Identity.Name, dependency.Identity.Version);

                if (package == null)
                {
                    continue;
                }

                foreach (var fileName in GetAssemblies(package, targetFramework))
                {
#if NET45 // CORECLR_TODO: AssemblyName.GetAssemblyName
                    var an = AssemblyName.GetAssemblyName(fileName);
                    _paths[an.Name] = fileName;
#else
                    _paths[Path.GetFileNameWithoutExtension(fileName)] = fileName;
#endif

                    if (!_paths.ContainsKey(package.Id))
                    {
                        _paths[package.Id] = fileName;
                    }
                }


                var sharedSources = GetSharedSources(package, targetFramework).ToList();
                if (!sharedSources.IsEmpty())
                {
                    _sharedSources[dependency.Identity.Name] = sharedSources;
                }
            }
        }

        public DependencyExport GetDependencyExport(string name, FrameworkName targetFramework)
        {
            if (!_dependencies.ContainsKey(name))
            {
                return null;
            }

            var paths = new HashSet<string>();

            PopulateDependenciesPaths(name, targetFramework, paths);

            var metadataReferenes = paths.Select(path => (IMetadataReference)new MetadataFileReference(path))
                                         .ToList();

            var sourceReferences = new List<ISourceReference>();

            IList<string> sharedSources;
            if (_sharedSources.TryGetValue(name, out sharedSources))
            {
                foreach (var sharedSource in sharedSources)
                {
                    sourceReferences.Add(new SourceFileReference(sharedSource));
                }
            }

            return new DependencyExport(metadataReferenes, sourceReferences);
        }

        private void PopulateDependenciesPaths(string name, FrameworkName targetFramework, ISet<string> paths)
        {
            DependencyDescription description;
            if (!_dependencies.TryGetValue(name, out description))
            {
                return;
            }

            string path;
            if (_paths.TryGetValue(name, out path))
            {
                paths.Add(path);
            }

            foreach (var dependency in description.Dependencies)
            {
                PopulateDependenciesPaths(dependency.Name, targetFramework, paths);
            }
        }

        private IEnumerable<string> GetAssemblies(IPackage package, FrameworkName frameworkName)
        {
            var path = _repository.PathResolver.GetInstallPath(package);

            var directory = Path.Combine(path, "lib", VersionUtility.GetShortFrameworkName(frameworkName));

            if (!Directory.Exists(directory))
            {
                return GetAssembliesFromPackage(package, frameworkName, path);
            }

            return Directory.EnumerateFiles(directory, "*.dll");
        }

        private IEnumerable<string> GetSharedSources(IPackage package, FrameworkName targetFramework)
        {
            var path = _repository.PathResolver.GetInstallPath(package);

            var directory = Path.Combine(path, "shared");

            return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
        }

        private static IEnumerable<string> GetAssembliesFromPackage(IPackage package, FrameworkName frameworkName, string path)
        {
            IEnumerable<IPackageAssemblyReference> references;
            if (VersionUtility.TryGetCompatibleItems(frameworkName, package.AssemblyReferences, out references))
            {
                foreach (var reference in references)
                {
                    string fileName = Path.Combine(path, reference.Path);
                    yield return fileName;
                }
            }
        }

        private IPackage FindCandidate(string name, SemanticVersion version)
        {
            if (version == null)
            {
                return _repository.FindPackagesById(name).FirstOrDefault();
            }
            else if (version.IsSnapshot)
            {
                return _repository.FindPackagesById(name)
                    .OrderByDescending(pk => pk.Version.SpecialVersion, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(pk => pk.Version.EqualsSnapshot(version));
            }
            return _repository.FindPackage(name, version);
        }

        private string ResolveRepositoryPath(string projectPath)
        {
            var di = new DirectoryInfo(projectPath);

            string rootPath = null;

            while (di.Parent != null)
            {
                if (di.EnumerateDirectories("packages").Any() ||
                    di.EnumerateFiles("*.sln").Any())
                {
                    rootPath = di.FullName;
                    break;
                }

                di = di.Parent;
            }

            rootPath = rootPath ?? Path.GetDirectoryName(projectPath);

            return Path.Combine(rootPath, "packages");
        }
    }
}

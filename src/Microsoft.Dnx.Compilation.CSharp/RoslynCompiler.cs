// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Dnx.Compilation.Caching;
using Microsoft.Dnx.Runtime;
using Microsoft.Dnx.Runtime.Common.DependencyInjection;

namespace Microsoft.Dnx.Compilation.CSharp
{
    public class RoslynCompiler
    {
        private readonly ICache _cache;
        private readonly ICacheContextAccessor _cacheContextAccessor;
        private readonly INamedCacheDependencyProvider _namedDependencyProvider;
        private readonly IAssemblyLoadContext _loadContext;
        private readonly IApplicationEnvironment _environment;
        private readonly IServiceProvider _services;
        private readonly Func<IMetadataFileReference, AssemblyMetadata> _assemblyMetadataFactory;

        public RoslynCompiler(ICache cache,
                              ICacheContextAccessor cacheContextAccessor,
                              INamedCacheDependencyProvider namedDependencyProvider,
                              IAssemblyLoadContext loadContext,
                              IApplicationEnvironment environment,
                              IServiceProvider services)
        {
            _cache = cache;
            _cacheContextAccessor = cacheContextAccessor;
            _namedDependencyProvider = namedDependencyProvider;
            _loadContext = loadContext;
            _environment = environment;
            _services = services;
            _assemblyMetadataFactory = fileReference =>
            {
                return _cache.Get<AssemblyMetadata>(fileReference.Path, ctx =>
                {
                    ctx.Monitor(new FileWriteTimeCacheDependency(fileReference.Path));
                    return fileReference.CreateAssemblyMetadata();
                });
            };
        }

        public CompilationContext CompileProject(
            CompilationProjectContext projectContext,
            IEnumerable<IMetadataReference> incomingReferences,
            IEnumerable<ISourceReference> incomingSourceReferences,
            Func<IList<ResourceDescriptor>> resourcesResolver)
        {
            var path = projectContext.ProjectDirectory;
            var name = projectContext.Target.Name;

            var isMainAspect = string.IsNullOrEmpty(projectContext.Target.Aspect);
            var isPreprocessAspect = string.Equals(projectContext.Target.Aspect, "preprocess", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(projectContext.Target.Aspect))
            {
                name += "!" + projectContext.Target.Aspect;
            }

            if (_cacheContextAccessor.Current != null)
            {
                _cacheContextAccessor.Current.Monitor(new FileWriteTimeCacheDependency(projectContext.ProjectFilePath));

                if (isMainAspect)
                {
                    // Monitor the trigger {projectName}_BuildOutputs
                    var buildOutputsName = projectContext.Target.Name + "_BuildOutputs";

                    _cacheContextAccessor.Current.Monitor(_namedDependencyProvider.GetNamedDependency(buildOutputsName));
                }

                _cacheContextAccessor.Current.Monitor(_namedDependencyProvider.GetNamedDependency(projectContext.Target.Name + "_Dependencies"));
            }

            var exportedReferences = incomingReferences
                .Select(reference => reference.ConvertMetadataReference(_assemblyMetadataFactory));

            Logger.TraceInformation("[{0}]: Compiling '{1}'", GetType().Name, name);
            var sw = Stopwatch.StartNew();

            var compilationSettings = projectContext.CompilerOptions.ToCompilationSettings(
                projectContext.Target.TargetFramework);

            var sourceFiles = Enumerable.Empty<string>();
            if (isMainAspect)
            {
                sourceFiles = projectContext.Files.SourceFiles;
            }
            else if (isPreprocessAspect)
            {
                sourceFiles = projectContext.Files.PreprocessSourceFiles;
            }

            var parseOptions = new CSharpParseOptions(languageVersion: compilationSettings.LanguageVersion,
                                                      preprocessorSymbols: compilationSettings.Defines);

            var trees = GetSyntaxTrees(
                projectContext,
                sourceFiles,
                incomingSourceReferences,
                parseOptions,
                isMainAspect);

            var references = new List<MetadataReference>();
            references.AddRange(exportedReferences);

            var compilation = CSharpCompilation.Create(
                name,
                trees,
                references,
                compilationSettings.CompilationOptions);

            compilation = ApplyVersionInfo(compilation, projectContext, parseOptions);

            var compilationContext = new CompilationContext(
                compilation,
                projectContext,
                incomingReferences,
                resourcesResolver);

            ValidateSigningOptions(compilationContext);
            AddStrongNameProvider(compilationContext);

            if (isMainAspect && projectContext.Files.PreprocessSourceFiles.Any())
            {
                try
                {
                    var modules = GetCompileModules(projectContext.Target).Modules;

                    foreach (var m in modules)
                    {
                        compilationContext.Modules.Add(m);
                    }
                }
                catch (Exception ex) when (ex.InnerException is RoslynCompilationException)
                {
                    var compilationException = ex.InnerException as RoslynCompilationException;

                    // Add diagnostics from the precompile step
                    foreach (var diag in compilationException.Diagnostics)
                    {
                        compilationContext.Diagnostics.Add(diag);
                    }
                }
            }

            if (compilationContext.Modules.Count > 0)
            {
                var precompSw = Stopwatch.StartNew();
                foreach (var module in compilationContext.Modules)
                {
                    module.BeforeCompile(compilationContext.BeforeCompileContext);
                }

                precompSw.Stop();
                Logger.TraceInformation("[{0}]: Compile modules ran in in {1}ms", GetType().Name, precompSw.ElapsedMilliseconds);
            }

            sw.Stop();
            Logger.TraceInformation("[{0}]: Compiled '{1}' in {2}ms", GetType().Name, name, sw.ElapsedMilliseconds);

            return compilationContext;
        }

        private void ValidateSigningOptions(CompilationContext compilationContext)
        {
            var compilerOptions = compilationContext.Project.CompilerOptions;

            var keyFile =
                Environment.GetEnvironmentVariable(EnvironmentNames.BuildKeyFile) ??
                compilerOptions.KeyFile;

            if (!string.IsNullOrEmpty(keyFile))
            {
                if (compilerOptions.StrongName == true)
                {
                    compilationContext.Diagnostics.Add(
                        Diagnostic.Create(RoslynDiagnostics.OssAndSnkSigningAreExclusive, null));
                    return;
                }

                if (RuntimeEnvironmentHelper.IsMono)
                {
                    compilationContext.Diagnostics.Add(
                        Diagnostic.Create(RoslynDiagnostics.SnkNotSupportedOnMono, null));
                    return;
                }
#if DNXCORE50
                compilationContext.Diagnostics.Add(
                    Diagnostic.Create(RoslynDiagnostics.StrongNamingNotSupported, null));
#endif
            }
        }

        private void AddStrongNameProvider(CompilationContext compilationContext)
        {
            if (!string.IsNullOrEmpty(compilationContext.Compilation.Options.CryptoKeyFile))
            {
                var strongNameProvider =
                    new DesktopStrongNameProvider(ImmutableArray.Create(compilationContext.Project.ProjectDirectory));

                compilationContext.Compilation =
                    compilationContext.Compilation.WithOptions(
                        compilationContext.Compilation.Options.WithStrongNameProvider(strongNameProvider));
            }
        }

        private CompilationModules GetCompileModules(CompilationTarget target)
        {
            // The only thing that matters is the runtime environment
            // when loading the compilation modules, so use that as the cache key
            var key = Tuple.Create(
                target.Name,
                _environment.RuntimeFramework,
                _environment.Configuration,
                "compilemodules");

            return _cache.Get<CompilationModules>(key, _ =>
            {
                var modules = new List<ICompileModule>();

                var preprocessAssembly = _loadContext.Load(new AssemblyName(target.Name + "!preprocess"));

                foreach (var preprocessType in preprocessAssembly.ExportedTypes)
                {
                    if (preprocessType.GetTypeInfo().ImplementedInterfaces.Contains(typeof(ICompileModule)))
                    {
                        var module = (ICompileModule)ActivatorUtilities.CreateInstance(_services, preprocessType);
                        modules.Add(module);
                    }
                }

                return new CompilationModules
                {
                    Modules = modules,
                };
            });
        }

        private static CSharpCompilation ApplyVersionInfo(CSharpCompilation compilation, CompilationProjectContext project,
            CSharpParseOptions parseOptions)
        {
            const string assemblyFileVersionName = "System.Reflection.AssemblyFileVersionAttribute";
            const string assemblyVersionName = "System.Reflection.AssemblyVersionAttribute";
            const string assemblyInformationalVersion = "System.Reflection.AssemblyInformationalVersionAttribute";

            var assemblyAttributes = compilation.Assembly.GetAttributes();

            var foundAssemblyFileVersion = false;
            var foundAssemblyVersion = false;
            var foundAssemblyInformationalVersion = false;

            foreach (var assembly in assemblyAttributes)
            {
                string attributeName = assembly.AttributeClass.ToString();

                if (string.Equals(attributeName, assemblyFileVersionName, StringComparison.Ordinal))
                {
                    foundAssemblyFileVersion = true;
                }
                else if (string.Equals(attributeName, assemblyVersionName, StringComparison.Ordinal))
                {
                    foundAssemblyVersion = true;
                }
                else if (string.Equals(attributeName, assemblyInformationalVersion, StringComparison.Ordinal))
                {
                    foundAssemblyInformationalVersion = true;
                }
            }

            var versionAttributes = new StringBuilder();
            if (!foundAssemblyFileVersion)
            {
                versionAttributes.AppendLine($"[assembly:{assemblyFileVersionName}(\"{project.AssemblyFileVersion}\")]");
            }

            if (!foundAssemblyVersion)
            {
                versionAttributes.AppendLine($"[assembly:{assemblyVersionName}(\"{RemovePrereleaseTag(project.Version)}\")]");
            }

            if (!foundAssemblyInformationalVersion)
            {
                versionAttributes.AppendLine($"[assembly:{assemblyInformationalVersion}(\"{project.Version}\")]");
            }

            if (versionAttributes.Length != 0)
            {
                compilation = compilation.AddSyntaxTrees(new[]
                {
                    CSharpSyntaxTree.ParseText(versionAttributes.ToString(), parseOptions)
                });
            }

            return compilation;
        }

        private static string RemovePrereleaseTag(string version)
        {
            // Simple reparse of the version string (because we don't want to pull in NuGet stuff
            // here because we're in an old-runtime/new-runtime limbo)

            var dashIdx = version.IndexOf('-');
            if (dashIdx < 0)
            {
                return version;
            }
            else
            {
                return version.Substring(0, dashIdx);
            }
        }

        private IList<SyntaxTree> GetSyntaxTrees(CompilationProjectContext project,
                                                 IEnumerable<string> sourceFiles,
                                                 IEnumerable<ISourceReference> sourceReferences,
                                                 CSharpParseOptions parseOptions,
                                                 bool isMainAspect)
        {
            var trees = new List<SyntaxTree>();

            var dirs = new HashSet<string>();

            if (isMainAspect)
            {
                dirs.Add(project.ProjectDirectory);
            }

            foreach (var sourcePath in sourceFiles)
            {
                var syntaxTree = CreateSyntaxTree(sourcePath, parseOptions);

                trees.Add(syntaxTree);
            }

            foreach (var sourceFileReference in sourceReferences.OfType<ISourceFileReference>())
            {
                var sourcePath = sourceFileReference.Path;

                var syntaxTree = CreateSyntaxTree(sourcePath, parseOptions);

                trees.Add(syntaxTree);
            }

            // Watch all directories
            var ctx = _cacheContextAccessor.Current;

            foreach (var d in dirs)
            {
                ctx.Monitor(new FileWriteTimeCacheDependency(d));
            }

            return trees;
        }

        private SyntaxTree CreateSyntaxTree(string sourcePath, CSharpParseOptions parseOptions)
        {
            // The cache key needs to take the parseOptions into account
            var cacheKey = sourcePath + string.Join(",", parseOptions.PreprocessorSymbolNames) + parseOptions.LanguageVersion;

            return _cache.Get<SyntaxTree>(cacheKey, ctx =>
            {
                ctx.Monitor(new FileWriteTimeCacheDependency(sourcePath));
                using (var stream = File.OpenRead(sourcePath))
                {
                    var sourceText = SourceText.From(stream, encoding: Encoding.UTF8);

                    return CSharpSyntaxTree.ParseText(sourceText, options: parseOptions, path: sourcePath);
                }
            });
        }

        private class CompilationModules
        {
            public List<ICompileModule> Modules { get; set; }
        }
    }
}

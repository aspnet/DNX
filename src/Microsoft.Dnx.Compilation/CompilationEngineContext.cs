﻿using System;
using Microsoft.Dnx.Compilation.Caching;
using Microsoft.Dnx.Runtime;
using Microsoft.Dnx.Runtime.Common.DependencyInjection;

namespace Microsoft.Dnx.Compilation
{
    public class CompilationEngineContext
    {
        public IFileWatcher FileWatcher { get; }
        public IServiceProvider Services { get { return _compilerServices; } }
        public IAssemblyLoadContext DefaultLoadContext { get; set; }
        public IApplicationEnvironment ApplicationEnvironment { get; set; }
        public IRuntimeEnvironment RuntimeEnvironment { get; set; }
        public CompilationCache CompilationCache { get; set; }

        private readonly ServiceProvider _compilerServices = new ServiceProvider();

        public CompilationEngineContext(IApplicationEnvironment applicationEnvironment,
                                        IRuntimeEnvironment runtimeEnvironment,
                                        IAssemblyLoadContext defaultLoadContext,
                                        CompilationCache cache) :
            this(applicationEnvironment, runtimeEnvironment, defaultLoadContext, cache, NoopWatcher.Instance)
        {

        }

        public CompilationEngineContext(IApplicationEnvironment applicationEnvironment,
                                        IRuntimeEnvironment runtimeEnvironment,
                                        IAssemblyLoadContext defaultLoadContext,
                                        CompilationCache cache,
                                        IFileWatcher fileWatcher)
        {
            ApplicationEnvironment = applicationEnvironment;
            RuntimeEnvironment = runtimeEnvironment;
            DefaultLoadContext = defaultLoadContext;
            CompilationCache = cache;
            FileWatcher = fileWatcher;

            // Register compiler services
            AddCompilationService(typeof(IFileWatcher), FileWatcher);
            AddCompilationService(typeof(IApplicationEnvironment), ApplicationEnvironment);
            AddCompilationService(typeof(ICache), CompilationCache.Cache);
            AddCompilationService(typeof(ICacheContextAccessor), CompilationCache.CacheContextAccessor);
            AddCompilationService(typeof(INamedCacheDependencyProvider), CompilationCache.NamedCacheDependencyProvider);
        }

        public void AddCompilationService(Type type, object instance)
        {
            _compilerServices.Add(type, instance);
        }
    }
}
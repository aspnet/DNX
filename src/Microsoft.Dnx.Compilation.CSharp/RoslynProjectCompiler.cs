using System;
using System.Collections.Generic;
using Microsoft.Dnx.Compilation.Caching;
using Microsoft.Dnx.Runtime;
using Microsoft.Dnx.Runtime.Infrastructure;

namespace Microsoft.Dnx.Compilation.CSharp
{
    public class RoslynProjectCompiler : IProjectCompiler
    {
        private readonly RoslynCompiler _compiler;

        public RoslynProjectCompiler(
            ICache cache,
            ICacheContextAccessor cacheContextAccessor,
            INamedCacheDependencyProvider namedCacheProvider,
            IAssemblyLoadContextFactory loadContextFactory,
            IFileWatcher watcher,
            IApplicationEnvironment environment,
            IServiceProvider services)
        {
            _compiler = new RoslynCompiler(
                cache,
                cacheContextAccessor,
                namedCacheProvider,
                loadContextFactory,
                watcher,
                environment,
                services);
        }

        public IMetadataProjectReference CompileProject(
            CompilationProjectContext projectContext,
            Func<LibraryExport> referenceResolver,
            Func<IList<ResourceDescriptor>> resourcesResolver)
        {
            var export = referenceResolver();
            if (export == null)
            {
                return null;
            }

            var incomingReferences = export.MetadataReferences;
            var incomingSourceReferences = export.SourceReferences;
            var analyzerReferences = export.AnalyzerReferences;

            var compliationContext = _compiler.CompileProject(
                projectContext,
                incomingReferences,
                incomingSourceReferences,
                analyzerReferences,
                resourcesResolver);

            if (compliationContext == null)
            {
                return null;
            }

            // Project reference
            return new RoslynProjectReference(compliationContext);
        }
    }
}

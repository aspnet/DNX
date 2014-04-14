﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.Net.Runtime.Roslyn
{
    public class CompilationContext
    {
        private RoslynLibraryExport _roslynLibraryExport;

        /// <summary>
        /// The project associated with this compilation
        /// </summary>
        public Project Project { get; private set; }

        // Processed information
        public CSharpCompilation Compilation { get; private set; }
        public IList<Diagnostic> Diagnostics { get; private set; }

        public IList<IMetadataReference> MetadataReferences { get; private set; }

        public CompilationContext(CSharpCompilation compilation,
                                  IList<IMetadataReference> metadataReferences,
                                  IList<Diagnostic> diagnostics,
                                  Project project)
        {
            Compilation = compilation;
            MetadataReferences = metadataReferences;
            Diagnostics = diagnostics;
            Project = project;
        }

        public RoslynLibraryExport GetLibraryExport()
        {
            if (_roslynLibraryExport == null)
            {
                var metadataReferences = new List<IMetadataReference>();
                var sourceReferences = new List<ISourceReference>();

                // Project reference
                metadataReferences.Add(new RoslynProjectReference(this));

                // Other references
                metadataReferences.AddRange(MetadataReferences);

                // Shared sources
                foreach (var sharedFile in Project.SharedFiles)
                {
                    sourceReferences.Add(new SourceFileReference(sharedFile));
                }

                _roslynLibraryExport = new RoslynLibraryExport(metadataReferences, sourceReferences, this);
            }

            return _roslynLibraryExport;
        }

        public IEnumerable<EmbeddedMetadataReference> GetRequiredEmbeddedReferences()
        {
            var assemblyNeutralTypes = MetadataReferences.OfType<EmbeddedMetadataReference>()
                                                         .ToDictionary(r => r.Name);

            // No assembly neutral types so do nothing
            if (assemblyNeutralTypes.Count == 0)
            {
                return Enumerable.Empty<EmbeddedMetadataReference>();
            }

            Trace.TraceInformation("Assembly Neutral References {0}", assemblyNeutralTypes.Count);
            var sw = Stopwatch.StartNew();

            // Walk the assembly neutral references and embed anything that we use
            // directly or indirectly
            var results = GetUsedReferences(assemblyNeutralTypes);


            // REVIEW: This should probably by driven by a property in the project metadata
            if (results.Count == 0)
            {
                // If nothing outgoing from this assembly, treat it like a carrier assembly
                // and embed everyting
                foreach (var a in assemblyNeutralTypes.Keys)
                {
                    results.Add(a);
                }
            }

            var embeddedTypes = results.Select(name => assemblyNeutralTypes[name])
                                       .ToList();

            sw.Stop();
            Trace.TraceInformation("Found {0} Assembly Neutral References in {1}ms", embeddedTypes.Count, sw.ElapsedMilliseconds);
            return embeddedTypes;
        }

        private HashSet<string> GetUsedReferences(Dictionary<string, EmbeddedMetadataReference> assemblies)
        {
            var results = new HashSet<string>();

            byte[] metadataBuffer = null;

            // First we need to emit just the metadata for this compilation
            using (var metadataStream = new MemoryStream())
            {
                var result = Compilation.Emit(metadataStream);

                if (!result.Success)
                {
                    // We failed to emit metadata so do nothing since we're probably
                    // going to fail compilation anyways
                    return results;
                }

                // Store the buffer and close the stream
                metadataBuffer = metadataStream.ToArray();
            }

            var stack = new Stack<Tuple<string, byte[]>>();
            stack.Push(Tuple.Create((string)null, metadataBuffer));

            while (stack.Count > 0)
            {
                var top = stack.Pop();

                var assemblyName = top.Item1;

                if (!String.IsNullOrEmpty(assemblyName) &&
                    !results.Add(assemblyName))
                {
                    // Skip the reference if saw it already
                    continue;
                }

                var buffer = top.Item2;

                foreach (var reference in GetReferences(buffer))
                {
                    EmbeddedMetadataReference embeddedReference;
                    if (assemblies.TryGetValue(reference, out embeddedReference))
                    {
                        stack.Push(Tuple.Create(reference, embeddedReference.Contents));
                    }
                }
            }

            return results;
        }

        private static IList<string> GetReferences(byte[] buffer)
        {
            var references = new List<string>();

            using (var stream = new MemoryStream(buffer))
            {
                var peReader = new PEReader(stream);

                var reader = peReader.GetMetadataReader();

                foreach (var a in reader.AssemblyReferences)
                {
                    var reference = reader.GetAssemblyReference(a);
                    var referenceName = reader.GetString(reference.Name);

                    references.Add(referenceName);
                }

                return references;
            }
        }
    }
}

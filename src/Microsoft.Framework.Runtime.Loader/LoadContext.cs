﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
#if ASPNET50
using System.Linq;
#endif
#if ASPNETCORE50
using System.Runtime.Loader;
#endif

namespace Microsoft.Framework.Runtime.Loader
{
#if ASPNETCORE50
    public abstract class LoadContext : AssemblyLoadContext, IAssemblyLoadContext
    {
        private readonly Dictionary<string, Assembly> _assemblyCache = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private readonly IAssemblyLoadContext _defaultContext;

        public LoadContext(IAssemblyLoadContext defaultContext)
        {
            _defaultContext = defaultContext;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;

            // TODO: Make this more efficient
            lock (_assemblyCache)
            {
                Assembly assembly;
                if (!_assemblyCache.TryGetValue(name, out assembly))
                {
                    assembly = LoadAssembly(name);

                    if (assembly != null)
                    {
                        _assemblyCache[name] = assembly;
                        ExtractAssemblyNeutralInterfaces(assembly);
                    }
                }

                return assembly;
            }
        }

        public Assembly Load(string name)
        {
            return LoadFromAssemblyName(new AssemblyName(name));
        }

        public abstract Assembly LoadAssembly(string name);

        public Assembly LoadFile(string path)
        {
            // Look for platform specific native image
            string nativeImagePath = GetNativeImagePath(path);

            if (nativeImagePath != null)
            {
                return LoadFromNativeImagePath(nativeImagePath, path);
            }

            return LoadFromAssemblyPath(path);
        }

        public Assembly LoadStream(Stream assembly, Stream assemblySymbols)
        {
            if (assemblySymbols == null)
            {
                return LoadFromStream(assembly);
            }

            return LoadFromStream(assembly, assemblySymbols);
        }

        private string GetNativeImagePath(string ilPath)
        {
            var directory = Path.GetDirectoryName(ilPath);
            var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            var nativeImageName = Path.GetFileNameWithoutExtension(ilPath) + ".ni.dll";
            var nativePath = Path.Combine(directory, arch, nativeImageName);

            if (File.Exists(nativePath))
            {
                return nativePath;
            }
            else
            {
                // Runtime is arch sensitive so the ni is in the same folder as IL
                nativePath = Path.Combine(directory, nativeImageName);
                if (File.Exists(nativePath))
                {
                    return nativePath;
                }
            }

            return null;
        }

        public void Dispose()
        {

        }

        private void ExtractAssemblyNeutralInterfaces(Assembly assembly)
        {
            // Embedded assemblies end with .dll
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.StartsWith("AssemblyNeutral/") &&
                    resourceName.EndsWith(".dll"))
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(resourceName);

                    var neutralAssemblyStream = assembly.GetManifestResourceStream(resourceName);

                    try
                    {
                        _defaultContext.LoadStream(neutralAssemblyStream, assemblySymbols: null);
                    }
                    catch (FileLoadException)
                    {
                        // Already loaded
                    }
                }
            }
        }
    }
#else
    public abstract class LoadContext : IAssemblyLoadContext
    {
        internal static LoadContext Default = new DefaultLoadContext();

        protected string _contextId;

        public LoadContext(IAssemblyLoadContext defaultContext)
        {
            _contextId = Guid.NewGuid().ToString();

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }

        public void Dispose()
        {
            // TODO: Remove instances of this type from the LoadContextAccessor
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
        }

        public Assembly Load(string name)
        {
            if (string.IsNullOrEmpty(_contextId))
            {
                return Assembly.Load(name);
            }

            return Assembly.Load(_contextId + "$" + name);
        }

        public abstract Assembly LoadAssembly(string name);

        public Assembly LoadFile(string assemblyPath)
        {
            return Assembly.LoadFile(assemblyPath);
        }

        public Assembly LoadStream(Stream assembly, Stream assemblySymbols)
        {
            byte[] assemblyBytes = GetStreamAsByteArray(assembly);
            byte[] assemblySymbolBytes = null;

            if (assemblySymbols != null)
            {
                assemblySymbolBytes = GetStreamAsByteArray(assemblySymbols);
            }

            return Assembly.Load(assemblyBytes, assemblySymbolBytes);
        }

        private byte[] GetStreamAsByteArray(Stream stream)
        {
            // Fast path assuming the stream is a memory stream
            var ms = stream as MemoryStream;
            if (ms != null)
            {
                return ms.ToArray();
            }

            // Otherwise copy the bytes
            using (ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            // {context}${name}
            var assemblyName = new AssemblyName(args.Name);

            var parts = assemblyName.Name.Split('$');

            if (parts.Length == 2)
            {
                string contextId = parts[0];
                string shortName = parts[1];

                if (string.Equals(contextId, _contextId, StringComparison.OrdinalIgnoreCase))
                {
                    var assembly = LoadAssembly(shortName);

                    if (assembly != null)
                    {
                        LoadContextAccessor.Instance.SetLoadContext(assembly, this);

                        return assembly;
                    }
                }
            }
            else
            {
                if (assemblyName.Name.EndsWith(".resources"))
                {
                    return null;
                }

                var requestingAssembly = args.RequestingAssembly;

#if ASPNET50
                // On Mono while loading the !preprocess assembly the ResolveEventArgs.RequestingAssembly is not populated. 
                // As a result when code in !preprocess references a class library the load fails on the class library.
                // Below is a work around to try populate the requesting assembly.

                if (Microsoft.Framework.Runtime.PlatformHelper.IsMono &&
                    requestingAssembly == null &&
                    !assemblyName.Name.Contains("!preprocess"))
                {
                    // See if this loading is for !preprocess.
                    var domain = (AppDomain)sender;

                    requestingAssembly = domain.GetAssemblies()
                        .Reverse()
                        .FirstOrDefault(a => a.FullName.Contains("!preprocess"));
                }
#endif

                if (requestingAssembly != null)
                {
                    // Get the relevant load context for the requesting assembly
                    var loadContext = LoadContextAccessor.Instance.GetLoadContext(requestingAssembly);
                    if (loadContext != null && loadContext != this && loadContext != Default)
                    {
                        return loadContext.Load(assemblyName.Name);
                    }
                }
            }

            return null;
        }

        private class DefaultLoadContext : LoadContext
        {
            public DefaultLoadContext()
                : base(defaultContext: null)
            {
                _contextId = null;
            }

            public override Assembly LoadAssembly(string name)
            {
                return null;
            }
        }
    }
#endif
}
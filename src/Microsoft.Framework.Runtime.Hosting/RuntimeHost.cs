﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Framework.Logging;
using Microsoft.Framework.Runtime.Common;
using Microsoft.Framework.Runtime.Dependencies;
using Microsoft.Framework.Runtime.Internal;
using NuGet.DependencyResolver;
using NuGet.Frameworks;

namespace Microsoft.Framework.Runtime
{
    public class RuntimeHost:IDisposable
    {
        private readonly ILogger Log;

        public Project Project { get; }
        public NuGetFramework TargetFramework { get; }
        public IEnumerable<IDependencyProvider> DependencyProviders { get; }
        public ILoggerFactory LoggerFactory { get; }
        public IEnumerable<IDisposable> LoaderDisposers { get; }
        public IServiceProvider Services { get; }

        internal RuntimeHost(RuntimeHostBuilder builder, IEnumerable<IDisposable> loaderDisposers)
        {
            if(builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if(builder.Project == null)
            {
                throw new ArgumentException($"{nameof(RuntimeHostBuilder)} does not contain a valid Project", nameof(builder));
            }

            Log = RuntimeLogging.Logger<RuntimeHost>();

            Project = builder.Project;
            Services = builder.Services;

            // Load properties from the mutable RuntimeHostBuilder into
            // immutable copies on this object
            TargetFramework = builder.TargetFramework;

            // Copy the dependency providers so the user can't fiddle with them without our knowledge
            var list = new List<IDependencyProvider>(builder.DependencyProviders);
            DependencyProviders = list;

            LoaderDisposers = loaderDisposers;
        }

        public Task<int> ExecuteApplication(string applicationName, string[] programArgs)
        {
            Log.LogInformation($"Launching '{applicationName}' '{string.Join(" ", programArgs)}'");

            var deps = DependencyManager.ResolveDependencies(
                DependencyProviders,
                Project.Name,
                Project.Version,
                TargetFramework);

            // Locate the entry point
            var entryPoint = LocateEntryPoint(applicationName);

            if (Log.IsEnabled(LogLevel.Information))
            {
                Log.LogInformation($"Executing Entry Point: {entryPoint.GetName().FullName}");
            }
            return EntryPointExecutor.Execute(entryPoint, programArgs, Services);
        }

        public void Dispose()
        {
            foreach(var loaderDisposer in LoaderDisposers)
            {
                loaderDisposer.Dispose();
            }
        }

        private Assembly LocateEntryPoint(string applicationName)
        {
            var sw = Stopwatch.StartNew();
            Log.LogInformation($"Locating entry point for {applicationName}");

            if (Project == null)
            {
                Log.LogError("Unable to locate entry point, there is no project");
                return null;
            }

            Assembly asm = null;
            try
            {
                asm = Assembly.Load(new AssemblyName(applicationName));
            }
            catch (FileLoadException ex) when (string.Equals(new AssemblyName(ex.FileName).Name, applicationName, StringComparison.Ordinal))
            {
                if (ex.InnerException is ICompilationException)
                {
                    throw ex.InnerException;
                }

                ThrowEntryPointNotFoundException(applicationName, ex);
            }
            catch (FileNotFoundException ex) when (string.Equals(ex.FileName, applicationName, StringComparison.Ordinal))
            {
                if (ex.InnerException is ICompilationException)
                {
                    throw ex.InnerException;
                }

                ThrowEntryPointNotFoundException(applicationName, ex);
            }

            sw.Stop();
            Log.LogInformation($"Located entry point in {sw.ElapsedMilliseconds}ms");

            return asm;
        }

        private void ThrowEntryPointNotFoundException(
            string applicationName,
            Exception innerException)
        {
            if (Project.Commands.Any())
            {
                // Throw a nicer exception message if the command
                // can't be found
                throw new InvalidOperationException(
                    string.Format("Unable to load application or execute command '{0}'. Available commands: {1}.",
                    applicationName,
                    string.Join(", ", Project.Commands.Keys)), innerException);
            }

            throw new InvalidOperationException(
                    string.Format("Unable to load application or execute command '{0}'.",
                    applicationName), innerException);
        }
    }
}

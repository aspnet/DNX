﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Framework.Logging;
using Microsoft.Framework.Runtime.Dependencies;
using Microsoft.Framework.Runtime.Internal;
using Microsoft.Framework.Runtime.Loader;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.ProjectModel;

namespace Microsoft.Framework.Runtime
{
    public class RuntimeHostBuilder
    {
        public IList<IDependencyProvider> DependencyProviders { get; } = new List<IDependencyProvider>();
        public NuGetFramework TargetFramework { get; set; }
        public Project Project { get; set; }
        public LockFile LockFile { get; set;  }
        public GlobalSettings GlobalSettings { get; set; }
        public IServiceProvider Services { get; set; }

        /// <summary>
        /// Create a <see cref="RuntimeHostBuilder"/> for the project in the specified
        /// <paramref name="projectDirectory"/>.
        /// </summary>
        /// <remarks>
        /// This method will throw if the project.json file cannot be found in the
        /// specified folder. If a project.lock.json file is present in the directory
        /// it will be loaded. 
        /// </remarks>
        /// <param name="projectDirectory">The directory of the project to host</param>
        public static RuntimeHostBuilder ForProjectDirectory(string projectDirectory, NuGetFramework runtimeFramework, IServiceProvider services)
        {
            if (string.IsNullOrEmpty(projectDirectory))
            {
                throw new ArgumentNullException(nameof(projectDirectory));
            }
            if (runtimeFramework == null)
            {
                throw new ArgumentNullException(nameof(runtimeFramework));
            }

            var log = RuntimeLogging.Logger<RuntimeHostBuilder>();
            using (log.LogTimedMethod())
            {
                var hostBuilder = new RuntimeHostBuilder();

                // Load the Project
                var projectResolver = new PackageSpecResolver(projectDirectory);
                PackageSpec packageSpec;
                if (projectResolver.TryResolvePackageSpec(GetProjectName(projectDirectory), out packageSpec))
                {
                    log.LogVerbose($"Loaded project {packageSpec.Name}");
                    hostBuilder.Project = new Project(packageSpec);
                }
                hostBuilder.GlobalSettings = projectResolver.GlobalSettings;

                // Load the Lock File if present
                LockFile lockFile;
                if (TryReadLockFile(projectDirectory, out lockFile))
                {
                    log.LogVerbose($"Loaded lock file");
                    hostBuilder.LockFile = lockFile;
                }

                // Set the framework
                hostBuilder.TargetFramework = runtimeFramework;

                hostBuilder.Services = services;

                log.LogVerbose("Registering PackageSpecReferenceDependencyProvider");
                hostBuilder.DependencyProviders.Add(new PackageSpecReferenceDependencyProvider(projectResolver));

                if (hostBuilder.LockFile != null)
                {
                    log.LogVerbose("Registering LockFileDependencyProvider");
                    hostBuilder.DependencyProviders.Add(new LockFileDependencyProvider(hostBuilder.LockFile));
                }

                log.LogVerbose("Registering ReferenceAssemblyDependencyProvider");
                var referenceResolver = new FrameworkReferenceResolver();
                hostBuilder.DependencyProviders.Add(new ReferenceAssemblyDependencyProvider(referenceResolver));

                // GAC resolver goes here! :)

                return hostBuilder;
            }
        }

        /// <summary>
        /// Builds a <see cref="RuntimeHost"/> from the parameters specified in this
        /// object.
        /// </summary>
        public RuntimeHost Build()
        {
            return new RuntimeHost(this);
        }

        private static string GetProjectName(string projectDirectory)
        {
            projectDirectory = projectDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return projectDirectory.Substring(Path.GetDirectoryName(projectDirectory).Length).Trim(Path.DirectorySeparatorChar);
        }

        private static bool TryReadLockFile(string directory, out LockFile lockFile)
        {
            lockFile = null;
            string file = Path.Combine(directory, LockFileFormat.LockFileName);
            if (File.Exists(file))
            {
                using (var stream = File.OpenRead(file))
                {
                    lockFile = LockFileFormat.Read(stream);
                }
                return true;
            }
            return false;
        }

    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Linq;
using Microsoft.Dnx.Runtime.Compilation;

namespace Microsoft.Dnx.Runtime.Loader
{
    public class ProjectAssemblyLoader : IAssemblyLoader
    {
        private readonly IAssemblyLoadContextAccessor _loadContextAccessor;
        private readonly ICompilationEngine _compilationEngine;
        private readonly IDictionary<string, RuntimeProject> _projects;

        public ProjectAssemblyLoader(IAssemblyLoadContextAccessor loadContextAccessor,
                                     ICompilationEngine compilationEngine,
                                     IEnumerable<ProjectDescription> projects)
        {
            _loadContextAccessor = loadContextAccessor;
            _compilationEngine = compilationEngine;
            _projects = projects.ToDictionary(p => p.Identity.Name, p => new RuntimeProject(p.Project, p.Framework));
        }

        public Assembly Load(AssemblyName assemblyName)
        {
            return Load(assemblyName, _loadContextAccessor.Default);
        }

        public Assembly Load(AssemblyName assemblyName, IAssemblyLoadContext loadContext)
        {
            // An assembly name like "MyLibrary!alternate!more-text"
            // is parsed into:
            // name == "MyLibrary"
            // aspect == "alternate"
            // and the more-text may be used to force a recompilation of an aspect that would
            // otherwise have been cached by some layer within Assembly.Load
            var name = assemblyName.Name;
            string aspect = null;
            var parts = name.Split(new[] { '!' }, 3);
            if (parts.Length != 1)
            {
                name = parts[0];
                aspect = parts[1];
            }

            if (!string.IsNullOrEmpty(assemblyName.CultureName) &&
                Path.GetExtension(name).Equals(".resources", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }

            RuntimeProject project;
            if (!_projects.TryGetValue(name, out project))
            {
                return null;
            }

            return _compilationEngine.LoadProject(
                project.Project,
                project.Framework,
                aspect,
                loadContext,
                assemblyName);
        }

        private struct RuntimeProject
        {
            public RuntimeProject(Project project, FrameworkName targetFramework)
            {
                Project = project;
                Framework = targetFramework;
            }

            public Project Project { get; }
            public FrameworkName Framework { get; }
        }
    }
}
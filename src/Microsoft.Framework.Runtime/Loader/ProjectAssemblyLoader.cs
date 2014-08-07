// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;

namespace Microsoft.Framework.Runtime.Loader
{
    internal class ProjectAssemblyLoader : IAssemblyLoader
    {
        private readonly IProjectResolver _projectResolver;
        private readonly ILibraryManager _libraryManager;
        private readonly IAssemblyLoaderEngine _loaderEngine;

        public ProjectAssemblyLoader(IProjectResolver projectResovler,
                                     IAssemblyLoaderEngine loaderEngine,
                                     ILibraryManager libraryManager)
        {
            _projectResolver = projectResovler;
            _loaderEngine = loaderEngine;
            _libraryManager = libraryManager;
        }

        public Assembly Load(string name)
        {
            Project project;
            if (!_projectResolver.TryResolveProject(name, out project))
            {
                return null;
            }

            var export = _libraryManager.GetLibraryExport(name);

            if (export == null)
            {
                return null;
            }

            foreach (var projectReference in export.MetadataReferences.OfType<IMetadataProjectReference>())
            {
                if (string.Equals(projectReference.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return projectReference.Load(_loaderEngine);
                }
            }

            return null;
        }
    }
}

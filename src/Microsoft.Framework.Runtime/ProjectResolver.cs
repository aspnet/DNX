// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Microsoft.Framework.Runtime
{
    public class ProjectResolver : IProjectResolver
    {
        private readonly HashSet<string> _searchPaths = new HashSet<string>();
        private readonly Dictionary<string, ProjectInformation> _projects = new Dictionary<string, ProjectInformation>();

        public ProjectResolver(string projectPath)
        {
            var rootPath = ResolveRootDirectory(projectPath);
            GlobalSettings global;
            GlobalSettings.TryGetGlobalSettings(rootPath, out global);
            Initialize(projectPath, global);
        }

        public ProjectResolver(string projectPath, GlobalSettings global)
        {
            Initialize(projectPath, global);
        }

        public IEnumerable<string> SearchPaths
        {
            get
            {
                return _searchPaths;
            }
        }

        public bool TryResolveProject(string name, out Project project)
        {
            project = null;

            ProjectInformation projectInfo;
            if (_projects.TryGetValue(name, out projectInfo))
            {
                project = projectInfo.Project;
                return project != null;
            }

            return false;
        }

        private void Initialize(string projectPath, GlobalSettings global)
        {
            _searchPaths.Add(new DirectoryInfo(projectPath).Parent.FullName);

            if (global != null)
            {
                foreach (var sourcePath in global.ProjectSearchPaths)
                {
                    _searchPaths.Add(Path.Combine(global.Directory, sourcePath));
                }
            }

            // Resolve all of the potential projects
            foreach (var searchPath in _searchPaths)
            {
                var directory = new DirectoryInfo(searchPath);

                if (!directory.Exists)
                {
                    continue;
                }

                foreach (var projectDirectory in directory.EnumerateDirectories())
                {
                    // The name of the folder is the project
                    _projects[projectDirectory.Name] = new ProjectInformation
                    {
                        Name = projectDirectory.Name,
                        FullPath = projectDirectory.FullName
                    };
                }
            }
        }

        public static string ResolveRootDirectory(string projectPath)
        {
            var di = new DirectoryInfo(projectPath);

            while (di.Parent != null)
            {
                var globalJsonPath = Path.Combine(di.FullName, GlobalSettings.GlobalFileName);

                if (File.Exists(globalJsonPath))
                {
                    return di.FullName;
                }

                di = di.Parent;
            }

            // If we don't find any files then make the project folder the root
            return projectPath;
        }

        private class ProjectInformation
        {
            private Project _project;
            private bool _initialized;
            private object _lockObj = new object();

            public string Name { get; set; }

            public string FullPath { get; set; }

            public Project Project
            {
                get
                {
                    return LazyInitializer.EnsureInitialized(ref _project, ref _initialized, ref _lockObj, () =>
                    {
                        Project project;
                        Project.TryGetProject(FullPath, out project);
                        return project;
                    });
                }
            }
        }
    }
}

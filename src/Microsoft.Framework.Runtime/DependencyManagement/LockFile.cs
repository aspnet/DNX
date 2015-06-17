// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Framework.Runtime.DependencyManagement
{
    public class LockFile
    {
        public bool Islocked { get; set; }
        public int Version { get; set; }
        public string Path { get; set; }
        public IList<ProjectFileDependencyGroup> ProjectFileDependencyGroups { get; set; } = new List<ProjectFileDependencyGroup>();
        public IList<LockFileLibrary> Libraries { get; set; } = new List<LockFileLibrary>();
        public IList<LockFileTarget> Targets { get; set; } = new List<LockFileTarget>();
        public IList<string> Diagnostics { get; private set; } = new List<string>();

        public void Validate(Project project)
        {
            Diagnostics.Clear();

            string message;
            if (!File.Exists(Path))
            {
                message = $"The expected lock file doesn't exist";
            }
            else if (IsValidForProject(project, out message))
            {
                return;
            }

            Diagnostics.Add(message);
        }

        private bool IsValidForProject(Project project, out string message)
        {
            if (Version != LockFileReader.Version)
            {
                message = $"The expected lock file version does not match the actual version";
                return false;
            }

            message = $"Dependencies in {Project.ProjectFileName} were modified";

            var actualTargetFrameworks = project.GetTargetFrameworks();

            // The lock file should contain dependencies for each framework plus dependencies shared by all frameworks
            if (ProjectFileDependencyGroups.Count != actualTargetFrameworks.Count() + 1)
            {
                return false;
            }

            foreach (var group in ProjectFileDependencyGroups)
            {
                IOrderedEnumerable<string> actualDependencies;
                var expectedDependencies = group.Dependencies.OrderBy(x => x);

                // If the framework name is empty, the associated dependencies are shared by all frameworks
                if (string.IsNullOrEmpty(group.FrameworkName))
                {
                    actualDependencies = project.Dependencies.Select(x => x.LibraryRange.ToString()).OrderBy(x => x);
                }
                else
                {
                    var framework = actualTargetFrameworks
                        .FirstOrDefault(f =>
                            string.Equals(f.FrameworkName.ToString(), group.FrameworkName, StringComparison.Ordinal));
                    if (framework == null)
                    {
                        return false;
                    }

                    actualDependencies = framework.Dependencies.Select(d => d.LibraryRange.ToString()).OrderBy(x => x);
                }

                if (!actualDependencies.SequenceEqual(expectedDependencies))
                {
                    return false;
                }
            }

            message = null;
            return true;
        }
    }
}
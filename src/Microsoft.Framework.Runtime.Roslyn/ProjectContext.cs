﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.Versioning;

namespace Microsoft.Framework.Runtime.Roslyn
{
    internal class ProjectContext : IProjectContext
    {
        public ProjectContext(Project project, FrameworkName targetFramework)
        {
            Name = project.Name;
            ProjectDirectory = project.ProjectDirectory;
            ProjectFilePath = project.ProjectFilePath;
            TargetFramework = targetFramework;
            Version = project.Version?.ToString();
        }

        public string Name { get; }
        public string Version { get; }
        public string ProjectDirectory { get; }
        public string ProjectFilePath { get; }
        public FrameworkName TargetFramework { get; }
    }
}
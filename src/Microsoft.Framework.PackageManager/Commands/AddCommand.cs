// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Framework.Runtime;
using Newtonsoft.Json.Linq;

namespace Microsoft.Framework.PackageManager
{
    public class AddCommand
    {
        public string Name;
        public string Version;
        public string ProjectDir;
        public IReport Report;

        public bool ExecuteCommand()
        {
            if (string.IsNullOrEmpty(Name))
            {
                Report.WriteLine("Name of dependency to add is required.");
                return false;
            }

            if (string.IsNullOrEmpty(Version))
            {
                Report.WriteLine("Version of dependency to add is required.");
                return false;
            }

            ProjectDir = ProjectDir ?? Directory.GetCurrentDirectory();

            Project project;
            if (!Project.TryGetProject(ProjectDir, out project))
            {
                Report.WriteLine("Unable to locate {0}.", Project.ProjectFileName);
                return false;
            }

            var root = JObject.Parse(File.ReadAllText(project.ProjectFilePath));
            if (root["dependencies"] == null)
            {
                root["dependencies"] = new JObject();
            }

            root["dependencies"][Name] = Version;

            File.WriteAllText(project.ProjectFilePath, root.ToString());

            return true;
        }
    }
}

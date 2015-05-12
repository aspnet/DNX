// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet;

namespace Microsoft.Framework.Runtime
{
    public class GlobalSettings
    {
        public const string GlobalFileName = "global.json";

        public IList<string> ProjectSearchPaths { get; private set; }
        public string PackagesPath { get; private set; }
        public string FilePath { get; private set; }
        public string Directory { get; private set; }
        public SemanticVersion SdkVersion { get; private set; }

        public static bool TryGetGlobalSettings(string path, out GlobalSettings globalSettings)
        {
            globalSettings = null;
            string globalJsonPath = null;

            if (Path.GetFileName(path) == GlobalFileName)
            {
                globalJsonPath = path;
                path = Path.GetDirectoryName(path);
            }
            else if (!HasGlobalFile(path))
            {
                return false;
            }
            else
            {
                globalJsonPath = Path.Combine(path, GlobalFileName);
            }

            globalSettings = new GlobalSettings();

            string json = File.ReadAllText(globalJsonPath);
            JObject settings = null;

            try
            {
                settings = JObject.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                throw FileFormatException.Create(ex, globalJsonPath);
            }

            // TODO: Remove sources
            var projectSearchPaths = settings["projects"] ?? settings["sources"];
            var sdkValue = settings["sdk"]?["version"]?.Value<string>();

            globalSettings.ProjectSearchPaths = projectSearchPaths == null ?
                new string[] { } :
                projectSearchPaths.ValueAsArray<string>();
            globalSettings.PackagesPath = settings.Value<string>("packages");
            globalSettings.FilePath = globalJsonPath;
            globalSettings.Directory = Path.GetDirectoryName(globalJsonPath);

            // If there's an exact version specified, parse it
            SemanticVersion sdkVersion;
            if (SemanticVersion.TryParse(sdkValue, out sdkVersion))
            {
                globalSettings.SdkVersion = sdkVersion;
            }

            return true;
        }

        public static bool HasGlobalFile(string path)
        {
            string projectPath = Path.Combine(path, GlobalFileName);

            return File.Exists(projectPath);
        }

    }
}

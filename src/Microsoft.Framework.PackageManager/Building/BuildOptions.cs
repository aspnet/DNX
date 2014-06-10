// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System.Runtime.Versioning;

namespace Microsoft.Framework.PackageManager
{
    public class BuildOptions
    {
        public string OutputDir { get; set; }
        public string ProjectDir { get; set; }
        public FrameworkName RuntimeTargetFramework { get; set; }
        public bool CheckDiagnostics { get; set; }
    }
}

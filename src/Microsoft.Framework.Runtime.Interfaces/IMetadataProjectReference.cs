// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Microsoft.Framework.Runtime
{
    [AssemblyNeutral]
    public interface IMetadataProjectReference : IMetadataReference
    {
        string ProjectPath { get; }

        IDiagnosticResult GetDiagnostics();

        IList<ISourceReference> GetSources();

        Assembly Load(IAssemblyLoaderEngine loaderEngine);

        void EmitReferenceAssembly(Stream stream);

        IDiagnosticResult EmitAssembly(string outputPath);
    }
}

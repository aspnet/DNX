// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.Versioning;

namespace Microsoft.Framework.Runtime.Roslyn
{
    public interface IRoslynCompiler
    {
        CompilationContext CompileProject(string name, FrameworkName targetFramework, string configuration);
    }
}

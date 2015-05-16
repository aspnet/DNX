// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Framework.Runtime
{
    /// <summary>
    /// Defines a contract for an assembly loader. This is an extension point that can be used to implement custom assembly loading logic.
    /// </summary>
    public interface IAssemblyLoader
    {
        /// <summary>
        /// Load an assembly by name.
        /// </summary>
        /// <param name="name">The name of the assembly.</param>
        /// <returns>The loaded assembly.</returns>
        //LOUDO: obsolete for finding usage
        //[Obsolete("Use IAssemblyLoader.Load(AssemblyName)")]
        Assembly Load(string name);
        Assembly Load(AssemblyName name);
    }
}

// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace Microsoft.Framework.Runtime
{
    public class UnresolvedDependencyProvider : IDependencyProvider
    {
        public LibraryDescription GetDescription(Library library, FrameworkName targetFramework)
        {
            return new LibraryDescription
            {
                Identity = library,
                Dependencies = Enumerable.Empty<LibraryDependency>(),
                Resolved = false
            };
        }

        public void Initialize(IEnumerable<LibraryDescription> dependencies, FrameworkName targetFramework)
        {
        }

        public IEnumerable<string> GetAttemptedPaths(FrameworkName targetFramework)
        {
            return Enumerable.Empty<string>();
        }
    }
}
﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using NuGet;

namespace Microsoft.Framework.Runtime
{
    public class TargetFrameworkInformation : IFrameworkTargetable
    {
        public FrameworkName FrameworkName { get; set; }

        public IList<Library> Dependencies { get; set; }

        public IEnumerable<FrameworkName> SupportedFrameworks
        {
            get
            {
                return new[] { FrameworkName };
            }
        }
    }
}
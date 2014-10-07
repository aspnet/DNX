// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Framework.PackageManager
{
    public class Reports
    {
        public IReport Information { get; set; }
        public IReport Verbose { get; set; }
        public IReport Quiet { get; set; }
        public IReport Error { get; set; }
    }
}
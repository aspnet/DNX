// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Microsoft.Framework.Runtime
{
    public interface IProjectReferenceProvider
    {
        IMetadataProjectReference GetProjectReference(
            Project project, 
            FrameworkName targetFramework, 
            string configuration, 
            IEnumerable<IMetadataReference> incomingReferences,
            IEnumerable<ISourceReference> incomingSourceReferences,
            IList<IMetadataReference> outgoingReferences);
    }
}
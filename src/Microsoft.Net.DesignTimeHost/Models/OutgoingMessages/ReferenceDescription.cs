﻿using System.Collections.Generic;
using Microsoft.Net.Runtime;

namespace Microsoft.Net.DesignTimeHost.Models.OutgoingMessages
{
    public class ReferenceDescription
    {
        public string Name { get; set; }

        public string Version { get; set; }

        public string Path { get; set; }

        public string Type { get; set; }

        public IEnumerable<ReferenceItem> Dependencies { get; set; }
    }
}
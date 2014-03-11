﻿using System.Collections.Generic;

namespace Microsoft.Net.DesignTimeHost.Models.OutgoingMessages
{
    public class SourcesMessage
    {
        public IList<string> Files { get; set; }
        public IDictionary<string, string> GeneratedFiles { get; set; }
    }
}

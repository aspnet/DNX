﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if NET45
using System.Resources;
#endif
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Microsoft.Net.Runtime.Roslyn
{
    public class ResxResourceProvider : IResourceProvider
    {
        public IList<ResourceDescription> GetResources(Project project)
        {
#if NET45 // CORECLR_TODO: ResourceWriter
            return Directory.EnumerateFiles(project.ProjectDirectory, "*.resx", SearchOption.AllDirectories)
                            .Select(resxFilePath =>
                                new ResourceDescription(GetResourceName(project.Name, resxFilePath),
                                                        () => GetResourceStream(resxFilePath),
                                                        isPublic: true)).ToList();
#else
            return new ResourceDescription[0];
#endif
        }
#if NET45 // CORECLR_TODO: ResourceWriter
        private static string GetResourceName(string projectName, string resxFilePath)
        {
            Trace.TraceInformation("Found resource {0}", resxFilePath);

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(resxFilePath);


            if (fileNameWithoutExtension.StartsWith(projectName, StringComparison.OrdinalIgnoreCase))
            {
                return fileNameWithoutExtension + ".resources";
            }

            return projectName + "." + fileNameWithoutExtension + ".resources";
        }

        private static Stream GetResourceStream(string resxFilePath)
        {
            using (var fs = File.OpenRead(resxFilePath))
            {
                var document = XDocument.Load(fs);

                var ms = new MemoryStream();
                var rw = new ResourceWriter(ms);

                foreach (var e in document.Root.Elements("data"))
                {
                    string name = e.Attribute("name").Value;
                    string value = e.Element("value").Value;

                    rw.AddResource(name, value);
                }

                rw.Generate();
                ms.Seek(0, SeekOrigin.Begin);

                return ms;
            }
        }
#endif

    }
}

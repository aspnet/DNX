﻿using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Net.Runtime;

namespace klr.host
{
    internal class ApplicationEnvironment : IApplicationEnvironment
    {
        public ApplicationEnvironment(string appBase, FrameworkName targetFramework, Assembly assembly)
        {
            var assemblyName = assembly.GetName();
            ApplicationName = assemblyName.Name;
            Version = assemblyName.Version.ToString();
            ApplicationBasePath = appBase;
            TargetFramework = targetFramework;
        }

        public string ApplicationName
        {
            get;
            private set;
        }

        public string Version
        {
            get;
            private set;
        }

        public string ApplicationBasePath
        {
            get;
            private set;
        }

        public FrameworkName TargetFramework
        {
            get;
            private set;
        }
    }
}

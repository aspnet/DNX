// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using NuGet;

namespace Microsoft.Framework.Runtime
{
    public class DependencyWalker
    {
        private readonly IEnumerable<IDependencyProvider> _dependencyProviders;
        private readonly List<LibraryDescription> _libraries = new List<LibraryDescription>();

        public DependencyWalker(IEnumerable<IDependencyProvider> dependencyProviders)
        {
            _dependencyProviders = dependencyProviders;
        }

        public IList<LibraryDescription> Libraries
        {
            get { return _libraries; }
        }

        public IEnumerable<IDependencyProvider> DependencyProviders
        {
            get
            {
                return _dependencyProviders;
            }
        }

        public void Walk(string name, SemanticVersion version, FrameworkName targetFramework)
        {
            var sw = Stopwatch.StartNew();
            Trace.TraceInformation("[{0}]: Walking dependency graph for '{1} {2}'.", GetType().Name, name, targetFramework);

            var context = new WalkContext();

            context.Walk(
                _dependencyProviders,
                name,
                version,
                targetFramework);

            context.Populate(targetFramework, Libraries);

            sw.Stop();
            Trace.TraceInformation("[{0}]: Resolved dependencies for {1} in {2}ms", GetType().Name, name, sw.ElapsedMilliseconds);
        }

        public string GetMissingDependenciesWarning(FrameworkName targetFramework)
        {
            var sb = new StringBuilder();

            // TODO: Localize messages

            sb.AppendFormat("Failed to resolve the following dependencies for target framework '{0}':", targetFramework.ToString());
            sb.AppendLine();

            foreach (var d in Libraries.Where(d => !d.Resolved).OrderBy(d => d.Identity.Name))
            {
                sb.AppendLine("   " + d.Identity.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("Searched Locations:");

            foreach (var path in GetAttemptedPaths(targetFramework))
            {
                sb.AppendLine("  " + path);
            }

            sb.AppendLine();
            sb.AppendLine("Try running 'kpm restore'.");

            return sb.ToString();
        }

        private IEnumerable<string> GetAttemptedPaths(FrameworkName targetFramework)
        {
            return DependencyProviders.SelectMany(p => p.GetAttemptedPaths(targetFramework));
        }
    }
}

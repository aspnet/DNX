﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Net.Runtime;
using NuGet;
using Shouldly;
using Xunit;

namespace Loader.Tests
{
    public class AssemblyLoaderFacts
    {
        Library[] Dependencies(Action<TestDependencyProvider.Entry> configure)
        {
            var entry = new TestDependencyProvider.Entry();
            configure(entry);
            return entry.Dependencies.ToArray();
        }

        [Fact]
        public void SimpleGraphCanBeWalked()
        {
            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("b", "1.0").Needs("c", "1.0"))
                .Package("b", "1.0")
                .Package("c", "1.0");

            var walker = new DependencyWalker(new[] { testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")
                .Needs("b", "1.0")
                .Needs("c", "1.0")));
        }


        [Fact]
        public void NestedGraphCanBeWalked()
        {
            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("b", "1.0").Needs("c", "1.0"))
                .Package("b", "1.0", that => that.Needs("c", "1.0").Needs("d", "1.0"))
                .Package("c", "1.0")
                .Package("d", "1.0");

            var walker = new DependencyWalker(new[]{ testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")
                .Needs("b", "1.0")
                .Needs("c", "1.0")
                .Needs("d", "1.0")));
        }


        [Fact]
        public void MissingDependenciesAreIgnored()
        {
            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("x", "1.0"));

            var walker = new DependencyWalker(new[]{ testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")));
        }

        [Fact]
        public void RecursiveDependenciesAreNotFollowed()
        {
            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("b", "1.0"))
                .Package("b", "1.0", that => that.Needs("c", "1.0"))
                .Package("c", "1.0", that => that.Needs("d", "1.0").Needs("b", "1.0"))
                .Package("d", "1.0", that => that.Needs("b", "1.0"));

            var walker = new DependencyWalker(new[]{ testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")
                .Needs("b", "1.0")
                .Needs("c", "1.0")
                .Needs("d", "1.0")));
        }

        [Fact]
        public void NearestDependencyVersionWins()
        {
            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("b", "1.0").Needs("c", "1.0").Needs("x", "1.0"))
                .Package("b", "1.0", that => that.Needs("x", "2.0"))
                .Package("c", "1.0", that => that.Needs("x", "2.0"))
                .Package("x", "1.0")
                .Package("x", "2.0");

            var walker = new DependencyWalker(new[]{ testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")
                .Needs("b", "1.0")
                .Needs("c", "1.0")
                .Needs("x", "1.0")
                ));
        }


        [Fact]
        public void HigherDisputedDependencyWins()
        {
            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("b", "1.0").Needs("c", "1.0"))
                .Package("b", "1.0", that => that.Needs("x", "1.0"))
                .Package("c", "1.0", that => that.Needs("x", "2.0"))
                .Package("x", "1.0")
                .Package("x", "2.0");

            var walker = new DependencyWalker(new[] { testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")
                .Needs("b", "1.0")
                .Needs("c", "1.0")
                .Needs("x", "2.0")
                ));
        }

        [Fact]
        public void RejectedDependenciesToNotCarryConstraints()
        {
            // a1->b1-*d1->e2->x2
            // a1->c1->d2
            // a1->c1->e1->x1
            // * b1->d1 lower than c1->d2 so d1->e2->x2 are n/a

            var testProvider = new TestDependencyProvider()
                .Package("a", "1.0", that => that.Needs("b", "1.0").Needs("c", "1.0"))
                .Package("b", "1.0", that => that.Needs("d", "1.0"))
                .Package("c", "1.0", that => that.Needs("d", "2.0").Needs("e", "1.0"))
                .Package("d", "1.0", that => that.Needs("e", "2.0"))
                .Package("d", "2.0")
                .Package("e", "1.0", that => that.Needs("x", "1.0"))
                .Package("e", "2.0", that => that.Needs("x", "2.0"))
                .Package("x", "1.0")
                .Package("x", "2.0");

            var walker = new DependencyWalker(new[] { testProvider });
            walker.Walk("a", new SemanticVersion("1.0"), VersionUtility.ParseFrameworkName("net45"));

            // the d1->e2->x2 line has no effect because d2 has no dependencies, 

            testProvider.Dependencies.ShouldBe(Dependencies(that => that
                .Needs("a", "1.0")
                .Needs("b", "1.0")
                .Needs("c", "1.0")
                .Needs("d", "2.0")
                .Needs("e", "1.0")
                .Needs("x", "1.0")
                ));
        }
    }

    public class TestDependencyProvider : IDependencyProvider
    {
        private readonly IDictionary<Library, Entry> _entries = new Dictionary<Library, Entry>();
        public IEnumerable<Library> Dependencies { get; set; }
        public FrameworkName FrameworkName { get; set; }

        public LibraryDescription GetDescription(string name, SemanticVersion version, FrameworkName frameworkName)
        {
            Trace.WriteLine(string.Format("StubAssemblyLoader.GetDependencies {0} {1} {2}", name, version, frameworkName));
            Entry entry;
            if (!_entries.TryGetValue(new Library { Name = name, Version = version }, out entry))
            {
                return null;
            }

            var d = entry.Dependencies as Library[] ?? entry.Dependencies.ToArray();
            Trace.WriteLine(string.Format("StubAssemblyLoader.GetDependencies {0} {1}", d.Aggregate("", (a, b) => a + " " + b), frameworkName));

            return new LibraryDescription
            {
                Identity = new Library { Name = name, Version = version },
                Dependencies = entry.Dependencies
            };
        }

        public void Initialize(IEnumerable<LibraryDescription> packages, FrameworkName frameworkName)
        {
            var d = packages.Select(package => package.Identity).ToArray();

            Trace.WriteLine(string.Format("StubAssemblyLoader.Initialize {0} {1}", d.Aggregate("", (a, b) => a + " " + b), frameworkName));

            Dependencies = d;
            FrameworkName = frameworkName;
        }

        public TestDependencyProvider Package(string name, string version)
        {
            return Package(name, version, _ => { });
        }

        public TestDependencyProvider Package(string name, string version, Action<Entry> configure)
        {
            var entry = new Entry { Key = new Library { Name = name, Version = new SemanticVersion(version) } };
            _entries[entry.Key] = entry;
            configure(entry);
            return this;
        }

        public class Entry
        {
            public Entry()
            {
                Dependencies = new List<Library>();
            }

            public Library Key { get; set; }
            public IList<Library> Dependencies { get; private set; }

            public Entry Needs(string name, string version)
            {
                Dependencies.Add(new Library { Name = name, Version = new SemanticVersion(version) });
                return this;
            }
        }

    }
}

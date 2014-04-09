using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.Versioning;
using Microsoft.Net.Runtime;
using Microsoft.Net.Runtime.Loader.NuGet;
using NuGet;
using System.Security.Cryptography;

namespace Microsoft.Net.Project.Packing
{
    public class PackRuntime
    {
        private readonly NuGetDependencyResolver _nugetDependencyResolver;
        private readonly Library _library;
        private readonly FrameworkName _frameworkName;

        public PackRuntime(
            NuGetDependencyResolver nugetDependencyResolver, 
            Library library,
            FrameworkName frameworkName)
        {
            _nugetDependencyResolver = nugetDependencyResolver;
            _library = library;
            _frameworkName = frameworkName;
        }

        public string Name { get; set; }
        public SemanticVersion Version { get; set; }

        public void Emit(PackRoot root)
        {
            var package = _nugetDependencyResolver.FindCandidate(
                _library.Name,
                _library.Version);

            Console.WriteLine("Packing runtime {0} {1}", package.Id, package.Version);

            Name = package.Id;
            Version = package.Version;

            var targetName = package.Id + "." + package.Version;
            var targetPath = Path.Combine(root.PackagesPath, targetName);

            if (Directory.Exists(targetPath))
            {
                Console.WriteLine("  {0} already exists.", targetPath);
                return;
            }

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            
            var targetNupkgPath = Path.Combine(targetPath, targetName + ".nupkg");
            using (var sourceStream = package.GetStream())
            {
                using (var archive = new ZipArchive(sourceStream, ZipArchiveMode.Read))
                {
                    root.Operations.ExtractNupkg(archive, targetPath);
                }
            }
            using (var sourceStream = package.GetStream())
            {
                using (var targetStream = new FileStream(targetNupkgPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    sourceStream.CopyTo(targetStream);
                }

                sourceStream.Seek(0, SeekOrigin.Begin);
                var sha512Bytes = SHA512.Create().ComputeHash(sourceStream);
                File.WriteAllText(targetNupkgPath + ".sha512", Convert.ToBase64String(sha512Bytes));
            }
        }
    }
}
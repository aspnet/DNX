﻿using NuGet;
using NuGet.Resources;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;

namespace NuGet
{
    /// <summary>
    /// Summary description for UnzippedPackage
    /// </summary>
    public class UnzippedPackage : LocalPackage
    {
        private Dictionary<string, PhysicalPackageFile> _files;
        private readonly IFileSystem _fileSystem;
        private readonly string _manifestPath;

        public UnzippedPackage(IFileSystem fileSystem, string manifestPath)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            if (String.IsNullOrEmpty(manifestPath))
            {
                throw new ArgumentNullException("manifestPath");
            }

            string manifestFullPath = fileSystem.GetFullPath(manifestPath);
            string directory = Path.GetDirectoryName(manifestFullPath);
            _fileSystem = new PhysicalFileSystem(directory);
            _manifestPath = Path.GetFileName(manifestFullPath);

            EnsureManifest();
        }

        private void EnsureManifest()
        {
            using (Stream stream = _fileSystem.OpenFile(_manifestPath))
            {
                ReadManifest(stream);
            }
        }

        protected override IEnumerable<IPackageFile> GetFilesBase()
        {
            EnsurePackageFiles();
            return _files.Values;
        }

        public override Stream GetStream()
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<IPackageAssemblyReference> GetAssemblyReferencesCore()
        {
            EnsurePackageFiles();

            return from file in _files.Values
                   where IsAssemblyReference(file.Path)
                   select (IPackageAssemblyReference)new PhysicalPackageAssemblyReference(file);
        }

        private void EnsurePackageFiles()
        {
            if (_files != null)
            {
                return;
            }

            _files = new Dictionary<string, PhysicalPackageFile>();
            foreach (var filePath in _fileSystem.GetFiles("", "*.*", true))
            {
                _files[filePath] = new PhysicalPackageFile
                {
                    SourcePath = _fileSystem.GetFullPath(filePath),
                    TargetPath = filePath
                };
            }
        }
    }
}
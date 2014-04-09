﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Net.Project.Packing
{
    public class PackOperations
    {
        public void Delete(string folderPath)
        {
            DeleteRecursive(folderPath);
        }

        private void DeleteRecursive(string deletePath)
        {
            if (!Directory.Exists(deletePath))
            {
                return;
            }

            foreach (var deleteFilePath in Directory.EnumerateFiles(deletePath).Select(Path.GetFileName))
            {
                File.Delete(Path.Combine(deletePath, deleteFilePath));
            }

            foreach (var deleteFolderPath in Directory.EnumerateDirectories(deletePath).Select(Path.GetFileName))
            {
                DeleteRecursive(Path.Combine(deletePath, deleteFolderPath));
                Directory.Delete(Path.Combine(deletePath, deleteFolderPath), recursive: true);
            }
        }

        public void Copy(string sourcePath, string targetPath)
        {
            CopyRecursive(
                sourcePath, 
                targetPath, 
                isProjectRootFolder: true, 
                shouldInclude: (_, __) => true);
        }

        public void Copy(string sourcePath, string targetPath, Func<bool, string, bool> shouldInclude)
        {
            CopyRecursive(
                sourcePath, 
                targetPath, 
                isProjectRootFolder: true,
                shouldInclude: shouldInclude);
        }

        private void CopyRecursive(string sourcePath, string targetPath, bool isProjectRootFolder, Func<bool, string, bool> shouldInclude)
        {
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            foreach (var sourceFilePath in Directory.EnumerateFiles(sourcePath))
            {
                var fileName = Path.GetFileName(sourceFilePath);
                Debug.Assert(fileName != null, "fileName != null");

                if (!shouldInclude(isProjectRootFolder, fileName))
                {
                    continue;
                }
                File.Copy(
                    Path.Combine(sourcePath, fileName),
                    Path.Combine(targetPath, fileName),
                    overwrite: true);
            }

            foreach (var sourceFolderPath in Directory.EnumerateDirectories(sourcePath))
            {
                var folderName = Path.GetFileName(sourceFolderPath);
                Debug.Assert(folderName != null, "folderName != null");

                if (!shouldInclude(isProjectRootFolder, folderName))
                {
                    continue;
                }

                CopyRecursive(
                    Path.Combine(sourcePath, folderName),
                    Path.Combine(targetPath, folderName),
                    isProjectRootFolder: false,
                    shouldInclude: shouldInclude);
            }
        }


        public void ExtractNupkg(ZipArchive archive, string targetPath)
        {
            ExtractFiles(
                archive, 
                targetPath, 
                shouldInclude: NupkgFilter);
        }

        private static bool NupkgFilter(string fullName)
        {
            var fileName = Path.GetFileName(fullName);
            if (fileName != null)
            {
                if (fileName == ".rels")
                {
                    return false;
                }
                if (fileName == "[Content_Types].xml")
                {
                    return false;
                }
            }

            var extension = Path.GetExtension(fullName);
            if (extension == ".psmdcp")
            {
                return false;
            }

            return true;
        }

        public void ExtractFiles(ZipArchive archive, string targetPath, Func<string, bool> shouldInclude)
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                var targetFile = Path.Combine(targetPath, entry.FullName);
                if (!targetFile.StartsWith(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!shouldInclude(entry.FullName))
                {
                    continue;
                }

                if (Path.GetFileName(targetFile).Length == 0)
                {
                    Directory.CreateDirectory(targetFile);
                }
                else
                {
                    var targetEntryPath = Path.GetDirectoryName(targetFile);
                    if (!Directory.Exists(targetEntryPath))
                    {
                        Directory.CreateDirectory(targetEntryPath);
                    }

                    using (var entryStream = entry.Open())
                    {
                        using (var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            entryStream.CopyTo(targetStream);
                        }
                    }
                }
            }
        }

        public void AddFiles(ZipArchive archive, string sourcePath, string targetPath, Func<string, string, bool> shouldInclude)
        {
            AddFilesRecursive(
                archive, 
                sourcePath, 
                "", 
                targetPath,
                shouldInclude);
        }

        private void AddFilesRecursive(ZipArchive archive, string sourceBasePath, string sourcePath, string targetPath, Func<string, string, bool> shouldInclude)
        {
            foreach (var fileName in Directory.EnumerateFiles(Path.Combine(sourceBasePath, sourcePath)).Select(Path.GetFileName))
            {
                if (!shouldInclude(sourcePath, fileName))
                {
                    continue;
                }
                var entry = archive.CreateEntry(Path.Combine(targetPath, fileName));
                using (var entryStream = entry.Open())
                {
                    using (var sourceStream = new FileStream(Path.Combine(sourceBasePath, sourcePath, fileName), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        sourceStream.CopyTo(entryStream);
                    }
                }
            }

            foreach (var folderName in Directory.EnumerateDirectories(Path.Combine(sourceBasePath, sourcePath)).Select(Path.GetFileName))
            {
                AddFilesRecursive(
                    archive,
                    sourceBasePath,
                    Path.Combine(sourcePath, folderName),
                    Path.Combine(targetPath, folderName),
                    shouldInclude);
            }
        }
    }
}

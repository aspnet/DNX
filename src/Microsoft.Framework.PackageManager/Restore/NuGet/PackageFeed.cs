// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Framework.Runtime;
using NuGet;

namespace Microsoft.Framework.PackageManager.Restore.NuGet
{
    public class PackageFeed : IPackageFeed
    {
        static readonly XName _xnameEntry = XName.Get("entry", "http://www.w3.org/2005/Atom");
        static readonly XName _xnameContent = XName.Get("content", "http://www.w3.org/2005/Atom");
        static readonly XName _xnameProperties = XName.Get("properties", "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");
        static readonly XName _xnameId = XName.Get("Id", "http://schemas.microsoft.com/ado/2007/08/dataservices");
        static readonly XName _xnameVersion = XName.Get("Version", "http://schemas.microsoft.com/ado/2007/08/dataservices");

        private readonly string _baseUri;
        private readonly IReport _report;
        private HttpSource _httpSource;
        TimeSpan _cacheAgeLimitList;
        TimeSpan _cacheAgeLimitNupkg;

        public PackageFeed(
            string baseUri,
            string userName,
            string password,
            bool cacheRefresh,
            IReport report)
        {
            _baseUri = baseUri.EndsWith("/") ? baseUri : (baseUri + "/");
            _report = report;
            _httpSource = new HttpSource(baseUri, userName, password, report);
            if (cacheRefresh)
            {
                _cacheAgeLimitList = TimeSpan.Zero;
                _cacheAgeLimitNupkg = TimeSpan.Zero;
            }
            else
            {
                _cacheAgeLimitList = TimeSpan.FromMinutes(30);
                _cacheAgeLimitNupkg = TimeSpan.FromHours(24);
            }
        }

        Dictionary<string, Task<IEnumerable<PackageInfo>>> _cache = new Dictionary<string, Task<IEnumerable<PackageInfo>>>();
        Dictionary<string, Task<NupkgEntry>> _cache2 = new Dictionary<string, Task<NupkgEntry>>();

        public Task<IEnumerable<PackageInfo>> FindPackagesByIdAsync(string id)
        {
            lock (_cache)
            {
                Task<IEnumerable<PackageInfo>> task;
                if (_cache.TryGetValue(id, out task))
                {
                    return task;
                }
                return _cache[id] = _FindPackagesByIdAsync(id);
            }
        }

        public async Task<IEnumerable<PackageInfo>> _FindPackagesByIdAsync(string id)
        {
            for (int retry = 0; retry != 3; ++retry)
            {
                try
                {
                    using (var data = await _httpSource.GetAsync(
                        _baseUri + "FindPackagesById()?Id='" + id + "'",
                        "list_" + id,
                        retry == 0 ? _cacheAgeLimitList : TimeSpan.Zero))
                    {
                        var doc = XDocument.Load(data.Stream);

                        var result = doc.Root
                            .Elements(_xnameEntry)
                            .Select(x => BuildModel(id, x))
                            .ToArray();

                        return result;
                    }
                }
                catch (Exception ex)
                {
                    if (retry == 2)
                    {
                        _report.WriteLine(string.Format("Error: FindPackagesById: {1}\r\n  {0}", ex.Message, id));
                        throw;
                    }
                    else
                    {
                        _report.WriteLine(string.Format("Warning: FindPackagesById: {1}\r\n  {0}", ex.Message, id));
                    }
                }
            }
            return null;
        }

        public PackageInfo BuildModel(string id, XElement element)
        {
            var properties = element.Element(_xnameProperties);

            return new PackageInfo
            {
                Id = id,
                Version = SemanticVersion.Parse(properties.Element(_xnameVersion).Value),
                ContentUri = element.Element(_xnameContent).Attribute("src").Value,
            };
        }

        public async Task<Stream> OpenNuspecStreamAsync(PackageInfo package)
        {
            using (var nupkgStream = await OpenNupkgStreamAsync(package))
            {
                if (PlatformHelper.IsMono)
                {
                    // Don't close the stream
                    var archive = Package.Open(nupkgStream, FileMode.Open, FileAccess.Read);
                    var partUri = PackUriHelper.CreatePartUri(new Uri(package.Id + ".nuspec", UriKind.Relative));
                    var entry = archive.GetPart(partUri);
                    using (var entryStream = entry.GetStream())
                    {
                        var nuspecStream = new MemoryStream((int)entryStream.Length);
                        await entryStream.CopyToAsync(nuspecStream);
                        nuspecStream.Seek(0, SeekOrigin.Begin);
                        return nuspecStream;
                    }
                }
                else
                {
                    using (var archive = new ZipArchive(nupkgStream, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        var entry = archive.GetEntry(package.Id + ".nuspec");
                        using (var entryStream = entry.Open())
                        {
                            var nuspecStream = new MemoryStream((int)entry.Length);
                            await entryStream.CopyToAsync(nuspecStream);
                            nuspecStream.Seek(0, SeekOrigin.Begin);
                            return nuspecStream;
                        }
                    }
                }
            }
        }

        public async Task<Stream> OpenNupkgStreamAsync(PackageInfo package)
        {
            Task<NupkgEntry> task;
            lock (_cache2)
            {
                if (!_cache2.TryGetValue(package.ContentUri, out task))
                {
                    task = _cache2[package.ContentUri] = _OpenNupkgStreamAsync(package);
                }
            }
            var result = await task;
            if (result == null)
            {
                return null;
            }
            return new FileStream(result.TempFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private async Task<NupkgEntry> _OpenNupkgStreamAsync(PackageInfo package)
        {
            for (int retry = 0; retry != 3; ++retry)
            {
                try
                {
                    using (var data = await _httpSource.GetAsync(
                        package.ContentUri,
                        "nupkg_" + package.Id + "." + package.Version,
                        retry == 0 ? _cacheAgeLimitNupkg : TimeSpan.Zero))
                    {
                        return new NupkgEntry
                        {
                            TempFileName = data.CacheFileName
                        };
                    }
                }
                catch (Exception ex)
                {
                    if (retry == 2)
                    {
                        _report.WriteLine(string.Format("Error: DownloadPackageAsync: {1}\r\n  {0}", ex.Message, package.ContentUri.Red().Bold()));
                    }
                    else
                    {
                        _report.WriteLine(string.Format("Warning: DownloadPackageAsync: {1}\r\n  {0}".Yellow().Bold(), ex.Message, package.ContentUri.Yellow().Bold()));
                    }
                }
            }
            return null;
        }

        class NupkgEntry
        {
            public string TempFileName { get; set; }
        }
    }
}


﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Dnx.Testing
{
    public class Dir
    {
        public const string EmptyFile = "";

        private readonly Dictionary<string, object> _nodes = new Dictionary<string, object>();
        private Func<FileInfo, object> _readFile;

        public Dir(string rootPath): this()
        {
            if (!Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException();
            }

            LoadPath = rootPath;
            Load(rootPath);
        }

        public Dir()
        {
            _readFile = fileInfo => new Lazy<object>(() => File.ReadAllText(fileInfo.FullName));
        }

        public Dir(Dir parent)
        {
            _readFile = parent._readFile;
        }

        public string LoadPath { get; private set; } = "In Memory";

        public Dictionary<string, object> Nodes
        {
            get
            {
                return _nodes;
            }
        }

        public object this[params string[] keys]
        {
            set
            {
                foreach (var key in keys)
                {
                    _nodes[key] = value;
                }
            }
        }

        public void Load(string path)
        {
            var directory = new DirectoryInfo(path);

            foreach (var subDirectoryInfo in directory.EnumerateDirectories())
            {
                Dir subDirectory = new Dir(this);
                this[subDirectoryInfo.Name] = subDirectory;
                subDirectory.Load(Path.Combine(path, subDirectoryInfo.Name));
            }

            foreach (var file in directory.EnumerateFiles())
            {
                this[file.Name] = _readFile(file);
            }
        }

        public void Save(string path)
        {
            Write(path, this);
        }

        private void Write(string path, object value)
        {
            var tree = value as Dir;
            if (tree != null)
            {
                Directory.CreateDirectory(path);

                foreach (var node in tree.Nodes)
                {
                    Write(Path.Combine(path, node.Key), node.Value);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, (value is Lazy<object> ? ((Lazy<object>)value)?.Value.ToString() : value.ToString()) ?? string.Empty);
            }
        }

        public DirDiff Diff(Dir other)
        {
            var nodes1 = Flatten();
            var nodes2 = other.Flatten();

            return new DirDiff
            {
                ExtraEntries = nodes1.Keys.Except(nodes2.Keys),
                MissingEntries = nodes2.Keys.Except(nodes1.Keys),
                DifferentEntries = nodes1.Keys.Intersect(nodes2.Keys)
                    .Where(entry => !string.Equals(
                        nodes1[entry] is Lazy<object> ? ((Lazy<object>)nodes1[entry])?.Value.ToString() : nodes1[entry].ToString(),
                        nodes2[entry] is Lazy<object> ? ((Lazy<object>)nodes2[entry])?.Value.ToString() : nodes2[entry].ToString(), 
                        StringComparison.Ordinal))
            };
        }

        public Dictionary<string, object> Flatten()
        {
            var allNodes = new Dictionary<string, object>();
            var stack = new Stack<Tuple<string, Dir>>();
            stack.Push(Tuple.Create(string.Empty, this));

            while (stack.Count > 0)
            {
                var top = stack.Pop();
                var basePath = top.Item1;
                var tree = top.Item2;

                foreach (var node in tree.Nodes)
                {
                    var subTree = node.Value as Dir;
                    var subPath = string.IsNullOrEmpty(basePath) ? node.Key : $"{basePath}/{node.Key}";
                    if (subTree == null)
                    {
                        allNodes[subPath] = node.Value;
                    }
                    else
                    {
                        stack.Push(Tuple.Create(subPath, subTree));
                    }
                }
            }

            return allNodes;
        }
    }
}
﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Framework.Runtime
{
    public class CompilerOptions
    {
        public IEnumerable<string> Defines { get; set; }

        public string LanguageVersion { get; set; }

        public string DebugSymbols { get; set; }

        public string Platform { get; set; }

        public bool? AllowUnsafe { get; set; }

        public bool? WarningsAsErrors { get; set; }

        public bool? Optimize { get; set; }

        public static CompilerOptions Combine(params CompilerOptions[] options)
        {
            var result = new CompilerOptions();

            foreach (var option in options)
            {
                // Skip null options
                if (option == null)
                {
                    continue;
                }

                // Defines are always combined
                if (option.Defines != null)
                {
                    var existing = result.Defines ?? Enumerable.Empty<string>();
                    result.Defines = existing.Concat(option.Defines).Distinct();
                }

                if (option.LanguageVersion != null)
                {
                    result.LanguageVersion = option.LanguageVersion;
                }

                if (option.DebugSymbols != null)
                {
                    result.DebugSymbols = option.DebugSymbols;
                }

                if (option.Platform != null)
                {
                    result.Platform = option.Platform;
                }

                if (option.AllowUnsafe != null)
                {
                    result.AllowUnsafe = option.AllowUnsafe;
                }

                if (option.WarningsAsErrors != null)
                {
                    result.WarningsAsErrors = option.WarningsAsErrors;
                }

                if (option.Optimize != null)
                {
                    result.Optimize = option.Optimize;
                }
            }

            return result;
        }
    }
}
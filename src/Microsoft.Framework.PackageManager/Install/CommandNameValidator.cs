﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;

namespace Microsoft.Framework.PackageManager
{
    public static class CommandNameValidator
    {
        private static readonly string[] BlockedCommandNames = new string[]
        {
            "dotnet",
            "dotnetsdk",
            "k",
            "dnx",
            "kpm",
            "dnvm",
            "nuget"
        };

        private static readonly string[] SkippedCommandNames = new string[]
        {
            "ef",
            "run",
            "test",
            "web"
        };

        public static bool IsCommandNameValid(string commandName)
        {
            // TODO: Make the comparison case sensitive of Linux?
            return
                !string.IsNullOrWhiteSpace(commandName) &&
                !BlockedCommandNames.Contains(commandName, StringComparer.OrdinalIgnoreCase);
        }

        public static bool ShouldNameBeSkipped(string commandName)
        {
            return SkippedCommandNames.Contains(commandName);
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.DotNet.Watcher.Tools
{
    public class DotNetWatchContext
    {
        public ProcessSpec ProcessSpec { get; set; }

        public int Iteration { get; set; }

        public string ChangedFile { get; set; }

        public bool RequiresMSBuildRevaluation { get; set; }
    }
}

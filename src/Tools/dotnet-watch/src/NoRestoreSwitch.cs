// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.Watcher.Tools
{
    public sealed class NoRestoreFilter : IWatchFilter
    {
        private bool _canUseNoRestore;
        private string[] _noRestoreArguments;

        public void Process(DotNetWatchContext context)
        {
            if (context.Iteration == 0)
            {
                // First iteration
                var arguments = context.ProcessSpec.Arguments;
                _canUseNoRestore = CanUseNoRestore(arguments);
                if (_canUseNoRestore)
                {
                    // Create run --no-restore <other args>
                    _noRestoreArguments = arguments.Take(1).Append("--no-restore").Concat(arguments.Skip(1)).ToArray();
                }
            }
            else if (_canUseNoRestore && !context.RequiresMSBuildRevaluation)
            {
                // For later iterations, do no restore as long as an MSBuild re-evaluation isn't required.
                context.ProcessSpec.Arguments = _noRestoreArguments;
            }
        }

        private static bool CanUseNoRestore(IEnumerable<string> arguments)
        {
            // For some well-known dotnet commands, we can pass in the --no-restore switch to avoid unnecessary restores between iterations.
            // For now we'll support the "run" and "test" commands.
            if (arguments.Any(a => string.Equals(a, "--no-restore", StringComparison.Ordinal)))
            {
                // Did the user already configure a --no-restore?
                return false;
            }

            var dotnetCommand = arguments.FirstOrDefault();
            return string.Equals(dotnetCommand, "run", StringComparison.Ordinal) ||
                string.Equals(dotnetCommand, "test", StringComparison.Ordinal);
        }
    }
}

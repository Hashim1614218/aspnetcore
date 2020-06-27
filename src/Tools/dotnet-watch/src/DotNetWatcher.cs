// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Watcher.Internal;
using Microsoft.DotNet.Watcher.Tools;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Tools.Internal;

namespace Microsoft.DotNet.Watcher
{
    public class DotNetWatcher
    {
        // File types that require an MSBuild re-evaluation
        private static readonly HashSet<string> _msBuildFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".props", ".targets", ".csproj", ".fsproj", ".vbproj",
        };
        private readonly IReporter _reporter;
        private readonly ProcessRunner _processRunner;
        private readonly IWatchFilter[] _filters = new[]
        {
            new NoRestoreFilter(),
        };

        public DotNetWatcher(IReporter reporter)
        {
            Ensure.NotNull(reporter, nameof(reporter));

            _reporter = reporter;
            _processRunner = new ProcessRunner(reporter);
        }

        public async Task WatchAsync(ProcessSpec processSpec, IFileSetFactory fileSetFactory,
            CancellationToken cancellationToken)
        {
            Ensure.NotNull(processSpec, nameof(processSpec));

            var cancelledTaskSource = new TaskCompletionSource();
            cancellationToken.Register(state => ((TaskCompletionSource)state).TrySetResult(),
                cancelledTaskSource);

            IFileSet fileSet = null;

            var initialArguments = processSpec.Arguments.ToArray();
            var context = new DotNetWatchContext
            {
                ProcessSpec = processSpec,
                ChangedFile = null,
            };

            while (true)
            {
                if (context.Iteration == 0 || context.RequiresMSBuildRevaluation)
                {
                    fileSet = await fileSetFactory.CreateAsync(cancellationToken);
                }

                // Reset arguments
                processSpec.Arguments = initialArguments;

                for (var i = 0; i < _filters.Length; i++)
                {
                    _filters[i].Process(context);
                }

                processSpec.EnvironmentVariables["DOTNET_WATCH_ITERATION"] = (context.Iteration + 1).ToString(CultureInfo.InvariantCulture);

                if (fileSet == null)
                {
                    _reporter.Error("Failed to find a list of files to watch");
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                using var currentRunCancellationSource = new CancellationTokenSource();
                using var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    currentRunCancellationSource.Token);

                using var fileSetWatcher = new FileSetWatcher(fileSet, _reporter);
                var fileChangedTask = fileSetWatcher.GetChangedFileAsync(combinedCancellationSource.Token);

                var processTask = _processRunner.RunAsync(processSpec, combinedCancellationSource.Token);

                var args = ArgumentEscaper.EscapeAndConcatenate(processSpec.Arguments);
                _reporter.Verbose($"Running {processSpec.ShortDisplayName()} with the following arguments: {args}");

                _reporter.Output("Started");

                var finishedTask = await Task.WhenAny(processTask, fileChangedTask, cancelledTaskSource.Task);

                // Regardless of the which task finished first, make sure everything is cancelled
                // and wait for dotnet to exit. We don't want orphan processes
                currentRunCancellationSource.Cancel();

                await Task.WhenAll(processTask, fileChangedTask);

                if (processTask.Result != 0 && finishedTask == processTask && !cancellationToken.IsCancellationRequested)
                {
                    // Only show this error message if the process exited non-zero due to a normal process exit.
                    // Don't show this if dotnet-watch killed the inner process due to file change or CTRL+C by the user
                    _reporter.Error($"Exited with error code {processTask.Result}");
                }
                else
                {
                    _reporter.Output("Exited");
                }

                if (finishedTask == cancelledTaskSource.Task || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (finishedTask == processTask)
                {
                    // Now wait for a file to change before restarting process
                    await fileSetWatcher.GetChangedFileAsync(cancellationToken, () => _reporter.Warn("Waiting for a file to change before restarting dotnet..."));
                }

                var changedFile = fileChangedTask.Result;
                if (!string.IsNullOrEmpty(changedFile))
                {
                    _reporter.Output($"File changed: {changedFile}");
                }

                context.RequiresMSBuildRevaluation = RequiresMSBuildRevaluation(changedFile);
                context.Iteration++;
            }
        }

        private static bool CanUseNoRestore(ProcessSpec processSpec)
        {
            // For some well-known dotnet commands, we can pass in the --no-restore switch to avoid unnecessary restores between iterations.
            // For now we'll support the "run" and "test" commands.
            if (processSpec.Arguments.Any(a => string.Equals(a, "--no-restore", StringComparison.Ordinal)))
            {
                // Did the user already configure a --no-restore?
                return false;
            }

            var dotnetCommand = processSpec.Arguments.FirstOrDefault();
            return string.Equals(processSpec.Executable, "run", StringComparison.Ordinal) ||
                string.Equals(processSpec.Executable, "test", StringComparison.Ordinal);
        }

        private static bool RequiresMSBuildRevaluation(string changedFile)
        {
            if (string.IsNullOrEmpty(changedFile))
            {
                return false;
            }

            var extension = Path.GetExtension(changedFile);
            return !string.IsNullOrEmpty(extension) && _msBuildFileExtensions.Contains(extension);
        }
    }
}

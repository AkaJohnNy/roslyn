﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Internal.Log
{
    /// <summary>
    /// a logger that aggregate multiple loggers
    /// </summary>
    internal sealed class AggregateLogger : ILogger
    {
        private readonly ImmutableArray<ILogger> loggers;

        public static AggregateLogger Create(params ILogger[] loggers)
        {
            var set = new HashSet<ILogger>();

            // flatten loggers
            foreach (var logger in loggers.WhereNotNull())
            {
                var aggregateLogger = logger as AggregateLogger;
                if (aggregateLogger != null)
                {
                    set.UnionWith(aggregateLogger.loggers);
                    continue;
                }

                set.Add(logger);
            }

            return new AggregateLogger(set.ToImmutableArray());
        }

        public static ILogger AddOrReplace(ILogger newLogger, ILogger oldLogger, Func<ILogger, bool> predicate)
        {
            if (newLogger == null)
            {
                return oldLogger;
            }

            var aggregateLogger = oldLogger as AggregateLogger;
            if (aggregateLogger == null)
            {
                // replace old logger with new logger
                if (predicate(oldLogger))
                {
                    // this might not aggregate logger
                    return newLogger;
                }

                // merge two
                return new AggregateLogger(ImmutableArray.Create(newLogger, oldLogger));
            }

            var set = new HashSet<ILogger>();

            foreach (var logger in aggregateLogger.loggers)
            {
                // replace this logger with new logger
                if (predicate(logger))
                {
                    set.Add(newLogger);
                    continue;
                }

                // add old one back
                set.Add(logger);
            }

            // add new logger. if we already added one, this will be ignored.
            set.Add(newLogger);
            return new AggregateLogger(set.ToImmutableArray());
        }

        private AggregateLogger(ImmutableArray<ILogger> loggers)
        {
            this.loggers = loggers;
        }

        public bool IsEnabled(FunctionId functionId)
        {
            return true;
        }

        public void Log(FunctionId functionId, LogMessage logMessage)
        {
            for (var i = 0; i < this.loggers.Length; i++)
            {
                var logger = this.loggers[i];
                if (!logger.IsEnabled(functionId))
                {
                    continue;
                }

                logger.Log(functionId, logMessage);
            }
        }

        public void LogBlockStart(FunctionId functionId, LogMessage logMessage, int uniquePairId, CancellationToken cancellationToken)
        {
            for (var i = 0; i < this.loggers.Length; i++)
            {
                var logger = this.loggers[i];
                if (!logger.IsEnabled(functionId))
                {
                    continue;
                }

                logger.LogBlockStart(functionId, logMessage, uniquePairId, cancellationToken);
            }
        }

        public void LogBlockEnd(FunctionId functionId, LogMessage logMessage, int uniquePairId, int delta, CancellationToken cancellationToken)
        {
            for (var i = 0; i < this.loggers.Length; i++)
            {
                var logger = this.loggers[i];
                if (!logger.IsEnabled(functionId))
                {
                    continue;
                }

                logger.LogBlockEnd(functionId, logMessage, uniquePairId, delta, cancellationToken);
            }
        }
    }
}

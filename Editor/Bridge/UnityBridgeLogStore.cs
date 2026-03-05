using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal sealed class UnityBridgeLogStore
    {
        private readonly object _lock = new object();
        private readonly List<BridgeLogEntry> _buffer;
        private readonly int _maxBufferSize;
        private long _nextId = 1;

        public UnityBridgeLogStore(int maxBufferSize)
        {
            _maxBufferSize = Math.Max(1, maxBufferSize);
            _buffer = new List<BridgeLogEntry>(Math.Min(256, _maxBufferSize));
        }

        public void Add(string condition, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                _buffer.Add(new BridgeLogEntry
                {
                    id = _nextId++,
                    level = MapLogType(type),
                    message = condition ?? string.Empty,
                    stackTrace = stackTrace ?? string.Empty,
                    timestampUtc = DateTime.UtcNow.ToString("O"),
                });

                int extra = _buffer.Count - _maxBufferSize;
                if (extra > 0)
                {
                    _buffer.RemoveRange(0, extra);
                }
            }
        }

        public LogSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new LogSnapshot(new List<BridgeLogEntry>(_buffer), _nextId - 1);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _buffer.Clear();
            }
        }

        private static string MapLogType(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return "warning";
                case LogType.Error:
                    return "error";
                case LogType.Exception:
                    return "exception";
                case LogType.Assert:
                    return "assert";
                default:
                    return "log";
            }
        }
    }

    internal sealed class LogSnapshot
    {
        public readonly List<BridgeLogEntry> entries;
        public readonly long maxId;

        public LogSnapshot(List<BridgeLogEntry> entries, long maxId)
        {
            this.entries = entries ?? new List<BridgeLogEntry>();
            this.maxId = maxId;
        }
    }
}

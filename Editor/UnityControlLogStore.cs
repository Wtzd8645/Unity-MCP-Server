using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal sealed class UnityControlLogStore
    {
        private readonly object _lock = new object();
        private readonly List<ControlLogEntry> _buffer;
        private readonly int _maxBufferSize;
        private long _nextId = 1;

        public UnityControlLogStore(int maxBufferSize)
        {
            _maxBufferSize = Math.Max(1, maxBufferSize);
            _buffer = new List<ControlLogEntry>(Math.Min(256, _maxBufferSize));
        }

        public void Add(string condition, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                _buffer.Add(new ControlLogEntry
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
                return new LogSnapshot(new List<ControlLogEntry>(_buffer), _nextId - 1);
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
            return type switch
            {
                LogType.Warning => "warning",
                LogType.Error => "error",
                LogType.Exception => "exception",
                LogType.Assert => "assert",
                _ => "log",
            };
        }
    }

    internal sealed class LogSnapshot
    {
        public readonly List<ControlLogEntry> entries;
        public readonly long maxId;

        public LogSnapshot(List<ControlLogEntry> entries, long maxId)
        {
            this.entries = entries ?? new List<ControlLogEntry>();
            this.maxId = maxId;
        }
    }
}

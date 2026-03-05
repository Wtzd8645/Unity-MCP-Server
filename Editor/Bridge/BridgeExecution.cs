#if UNITY_EDITOR
using System;

namespace Blanketmen.UnityMcpBridge.Editor
{
    internal delegate bool MainThreadActionInvoker(Action action, int timeoutMs, out string error);
}
#endif


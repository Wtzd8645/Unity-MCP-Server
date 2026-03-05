using System;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal delegate bool MainThreadActionInvoker(Action action, int timeoutMs, out string error);
}

using System;

namespace Blanketmen.UnityMcp.Control.Editor
{
    internal delegate bool MainThreadActionInvoker(Action action, int timeoutMs, out string error);
}

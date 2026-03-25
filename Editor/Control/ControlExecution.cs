using System;

namespace Blanketmen.UnityMcp.Editor.Control
{
    internal delegate bool MainThreadActionInvoker(Action action, int timeoutMs, out string error);
}

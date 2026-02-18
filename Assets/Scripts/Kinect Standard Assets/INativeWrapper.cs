using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Helper
{
    internal interface INativeWrapper
    {
        System.IntPtr nativePtr { get; }
    }
}

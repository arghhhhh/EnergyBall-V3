using System.Collections.Generic;
using System.Linq;
using RootSystem = System;

namespace Windows.Kinect
{
    //
    // Windows.Kinect.FrameCapturedStatus
    //
    public enum FrameCapturedStatus : int
    {
        Unknown = 0,
        Queued = 1,
        Dropped = 2,
    }
}

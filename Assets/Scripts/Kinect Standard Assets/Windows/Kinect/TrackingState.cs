using System.Collections.Generic;
using System.Linq;
using RootSystem = System;

namespace Windows.Kinect
{
    //
    // Windows.Kinect.TrackingState
    //
    public enum TrackingState : int
    {
        NotTracked = 0,
        Inferred = 1,
        Tracked = 2,
    }
}

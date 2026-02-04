using System.Collections.Generic;
using System.Linq;
using RootSystem = System;

namespace Windows.Kinect
{
    //
    // Windows.Kinect.Activity
    //
    public enum Activity : int
    {
        EyeLeftClosed = 0,
        EyeRightClosed = 1,
        MouthOpen = 2,
        MouthMoved = 3,
        LookingAway = 4,
    }
}

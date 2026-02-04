using System.Collections.Generic;
using System.Linq;
using RootSystem = System;

namespace Windows.Kinect
{
    //
    // Windows.Kinect.ColorImageFormat
    //
    public enum ColorImageFormat : int
    {
        None = 0,
        Rgba = 1,
        Yuv = 2,
        Bgra = 3,
        Bayer = 4,
        Yuy2 = 5,
    }
}

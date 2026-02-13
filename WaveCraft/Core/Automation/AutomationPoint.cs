using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaveCraft.Core.Automation
{
    public record AutomationPoint(
        long Frame,         // Position on the timeline
        float Value,        // Parameter value at this point
        CurveType Curve);   // Interpolation to the next point

    public enum CurveType
    {
        Linear,
        Exponential,
        SCurve,
        Step    // Jump instantly to the next value
    }
}

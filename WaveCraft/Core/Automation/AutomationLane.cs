namespace WaveCraft.Core.Automation
{
    /// <summary>
    /// An automation lane stores a sorted list of keyframes and
    /// interpolates between them to produce smooth parameter changes.
    ///
    /// PROFESSIONAL CONCEPT: Automation is how DAWs record and replay
    /// fader movements, effect knob turns, etc. The lane evaluates at
    /// any frame position and returns the interpolated value.
    /// </summary>
    public class AutomationLane
    {
        public string ParameterName { get; set; } = "";
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public float DefaultValue { get; set; }

        private readonly List<AutomationPoint> _points = new();
        public IReadOnlyList<AutomationPoint> Points => _points;

        public void AddPoint(AutomationPoint point)
        {
            // Keep sorted by frame position
            int index = _points.FindIndex(p => p.Frame >= point.Frame);
            if (index >= 0 && _points[index].Frame == point.Frame)
                _points[index] = point; // Replace existing at same position
            else if (index >= 0)
                _points.Insert(index, point);
            else
                _points.Add(point);
        }

        public void RemovePoint(int index)
        {
            if (index >= 0 && index < _points.Count)
                _points.RemoveAt(index);
        }

        /// <summary>
        /// Evaluate the automation value at a given frame position.
        /// Interpolates between surrounding keyframes.
        /// </summary>
        public float Evaluate(long frame)
        {
            if (_points.Count == 0) return DefaultValue;
            if (_points.Count == 1) return _points[0].Value;

            // Before first point
            if (frame <= _points[0].Frame) return _points[0].Value;

            // After last point
            if (frame >= _points[^1].Frame) return _points[^1].Value;

            // Find surrounding points
            for (int i = 0; i < _points.Count - 1; i++)
            {
                var a = _points[i];
                var b = _points[i + 1];

                if (frame >= a.Frame && frame < b.Frame)
                {
                    float t = (float)(frame - a.Frame) / (b.Frame - a.Frame);
                    return Interpolate(a.Value, b.Value, t, a.Curve);
                }
            }

            return DefaultValue;
        }

        private static float Interpolate(float a, float b, float t, CurveType curve)
        {
            return curve switch
            {
                CurveType.Linear => a + (b - a) * t,
                CurveType.Exponential => a + (b - a) * t * t,
                CurveType.SCurve => a + (b - a) * (t * t * (3f - 2f * t)),
                CurveType.Step => a,
                _ => a + (b - a) * t
            };
        }
    }
}
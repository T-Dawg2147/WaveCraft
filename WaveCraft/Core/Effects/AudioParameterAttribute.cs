namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Custom attribute that marks a property as an automatable audio parameter.
    /// 
    /// PROFESSIONAL CONCEPT: Custom attributes let you declaratively annotate
    /// code with metadata. At runtime, reflection reads these attributes to
    /// auto-generate UI controls for each effect parameter — you never write
    /// a manual slider for gain, delay time, etc.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class AudioParameterAttribute : Attribute
    {
        /// <summary>Display name shown in the UI.</summary>
        public string Name { get; }

        /// <summary>Minimum value for the slider/knob.</summary>
        public float MinValue { get; }

        /// <summary>Maximum value for the slider/knob.</summary>
        public float MaxValue { get; }

        /// <summary>Default value when the effect is first loaded.</summary>
        public float DefaultValue { get; }

        /// <summary>Unit label: "dB", "ms", "%", "Hz", etc.</summary>
        public string Unit { get; }

        /// <summary>If true, the UI shows a logarithmic scale (common for frequency/dB).</summary>
        public bool IsLogarithmic { get; }

        public AudioParameterAttribute(string name,
            float minValue, float maxValue, float defaultValue,
            string unit = "", bool isLogarithmic = false)
        {
            Name = name;
            MinValue = minValue;
            MaxValue = maxValue;
            DefaultValue = defaultValue;
            Unit = unit;
            IsLogarithmic = isLogarithmic;
        }
    }
}
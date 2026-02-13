using System.Linq.Expressions;
using System.Reflection;
using WaveCraft.Core.Effects;

namespace WaveCraft.Core.Automation
{
    /// <summary>
    /// Compiles expression trees for fast property access during automation.
    ///
    /// PROFESSIONAL CONCEPT: Expression trees let you build and compile
    /// code at runtime. Instead of using slow reflection (PropertyInfo.SetValue)
    /// every audio frame, we compile a fast delegate once and call it
    /// thousands of times per second with zero overhead.
    ///
    /// This is the same technique used by ORMs (Entity Framework),
    /// serializers, and dependency injection frameworks.
    /// </summary>
    public static class AutomationCompiler
    {
        /// <summary>
        /// Compile a fast getter delegate for a property.
        /// </summary>
        public static Func<object, float> CompileGetter(Type targetType, string propertyName)
        {
            var param = Expression.Parameter(typeof(object), "target");
            var castTarget = Expression.Convert(param, targetType);
            var property = Expression.Property(castTarget, propertyName);
            var castResult = Expression.Convert(property, typeof(float));

            return Expression.Lambda<Func<object, float>>(castResult, param).Compile();
        }

        /// <summary>
        /// Compile a fast setter delegate for a property.
        /// </summary>
        public static Action<object, float> CompileSetter(Type targetType, string propertyName)
        {
            var targetParam = Expression.Parameter(typeof(object), "target");
            var valueParam = Expression.Parameter(typeof(float), "value");
            var castTarget = Expression.Convert(targetParam, targetType);
            var property = Expression.Property(castTarget, propertyName);
            var assign = Expression.Assign(property, valueParam);

            return Expression.Lambda<Action<object, float>>(assign, targetParam, valueParam)
                .Compile();
        }

        /// <summary>
        /// Discovers all [AudioParameter] properties on an effect and
        /// compiles getter/setter delegates for each one.
        /// </summary>
        public static List<CompiledParameter> CompileEffectParameters(IAudioEffect effect)
        {
            var results = new List<CompiledParameter>();
            var type = effect.GetType();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<AudioParameterAttribute>();
                if (attr == null) continue;

                results.Add(new CompiledParameter
                {
                    Name = attr.Name,
                    PropertyName = prop.Name,
                    MinValue = attr.MinValue,
                    MaxValue = attr.MaxValue,
                    DefaultValue = attr.DefaultValue,
                    Unit = attr.Unit,
                    IsLogarithmic = attr.IsLogarithmic,
                    Getter = CompileGetter(type, prop.Name),
                    Setter = CompileSetter(type, prop.Name),
                    Target = effect
                });
            }

            return results;
        }
    }

    /// <summary>
    /// A compiled parameter — holds the fast delegates and metadata
    /// for a single automatable property on an effect.
    /// </summary>
    public class CompiledParameter
    {
        public string Name { get; init; } = "";
        public string PropertyName { get; init; } = "";
        public float MinValue { get; init; }
        public float MaxValue { get; init; }
        public float DefaultValue { get; init; }
        public string Unit { get; init; } = "";
        public bool IsLogarithmic { get; init; }

        public Func<object, float> Getter { get; init; } = null!;
        public Action<object, float> Setter { get; init; } = null!;
        public object Target { get; init; } = null!;

        /// <summary>Read the current value using the compiled delegate.</summary>
        public float GetValue() => Getter(Target);

        /// <summary>Set the value using the compiled delegate.</summary>
        public void SetValue(float value)
        {
            value = Math.Clamp(value, MinValue, MaxValue);
            Setter(Target, value);
        }
    }
}
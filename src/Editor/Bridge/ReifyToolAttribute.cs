using System;

namespace Reify.Editor.Bridge
{
    /// <summary>
    /// Marks a static method as a reify tool handler. The bridge scans the
    /// Reify.Editor assembly on load, finds every method decorated with
    /// this attribute, and registers it under the given kebab-case tool
    /// name. This removes the need to maintain a manual Register(...) list
    /// in ReifyBridge — the per-tool file is the single source of truth.
    ///
    /// The decorated method must match the signature
    /// <c>public static Task&lt;object&gt; X(JToken args)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class ReifyToolAttribute : Attribute
    {
        public string Name { get; }
        public ReifyToolAttribute(string name) { Name = name; }
    }
}

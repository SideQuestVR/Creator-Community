// SideQuest Lighting Tools - MIT
using System.Collections.Generic;

namespace SideQuest.LightingTools.Core
{
    public enum SqSeverity { Info, Warn, Error }

    /// <summary>
    /// A pre-digested finding about the scene.
    ///
    /// problems[] and recommendations[] are the highest-value part of a report. Handing an
    /// LLM a raw dump of 500 renderers and expecting it to notice that 40 of them have no
    /// lightmap UVs is expensive and unreliable; handing it one Problem saying exactly
    /// that, with a suggested action, is neither.
    ///
    /// Codes are stable and namespaced by system (LP, RP, OC, LB, URP) so they can be
    /// referenced in documentation and matched in logs.
    /// </summary>
    public sealed class SqProblem
    {
        public string Code;
        public SqSeverity Severity;
        public string Message;

        /// <summary>How many objects this applies to, when it is a per-object finding.</summary>
        public int Count;

        /// <summary>Report-local object indices, capped - enough to investigate, not a full list.</summary>
        public List<int> SampleIndices;

        public string SuggestedAction;

        /// <summary>
        /// True when the finding depends on a URP asset setting the Banter client owns,
        /// so acting on it locally may change nothing in the shipped world.
        /// </summary>
        public bool DependsOnClientUrpAsset;

        public const int MaxSamples = 8;

        public SqProblem(string code, SqSeverity severity, string message)
        {
            Code = code;
            Severity = severity;
            Message = message;
        }

        public SqProblem WithCount(int count) { Count = count; return this; }
        public SqProblem WithAction(string action) { SuggestedAction = action; return this; }
        public SqProblem ClientDependent() { DependsOnClientUrpAsset = true; return this; }

        public SqProblem WithSample(int index)
        {
            if (SampleIndices == null) SampleIndices = new List<int>(MaxSamples);
            if (SampleIndices.Count < MaxSamples) SampleIndices.Add(index);
            return this;
        }

        public SqProblem WithSamples(IEnumerable<int> indices)
        {
            foreach (int i in indices)
            {
                if (SampleIndices != null && SampleIndices.Count >= MaxSamples) break;
                WithSample(i);
            }
            return this;
        }

        public static string SeverityName(SqSeverity s)
        {
            switch (s)
            {
                case SqSeverity.Error: return "error";
                case SqSeverity.Warn: return "warn";
                default: return "info";
            }
        }

        public void Write(SqJsonWriter w)
        {
            w.BeginObject();
            w.Prop("code", Code);
            w.Prop("severity", SeverityName(Severity));
            w.Prop("message", Message);
            if (Count > 0) w.Prop("count", Count);
            if (SampleIndices != null && SampleIndices.Count > 0) w.Prop("sampleIdx", SampleIndices);
            if (!string.IsNullOrEmpty(SuggestedAction)) w.Prop("suggestedAction", SuggestedAction);
            if (DependsOnClientUrpAsset) w.Prop("dependsOnClientUrpAsset", true);
            w.EndObject();
        }
    }

    /// <summary>Collects findings and writes them ordered by severity, worst first.</summary>
    public sealed class SqProblemList
    {
        readonly List<SqProblem> _items = new List<SqProblem>();

        public int Count { get { return _items.Count; } }
        public IReadOnlyList<SqProblem> Items { get { return _items; } }

        public SqProblem Add(SqProblem problem)
        {
            if (problem != null) _items.Add(problem);
            return problem;
        }

        public SqProblem Add(string code, SqSeverity severity, string message)
        {
            return Add(new SqProblem(code, severity, message));
        }

        public bool HasError
        {
            get
            {
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].Severity == SqSeverity.Error) return true;
                return false;
            }
        }

        public int CountOf(SqSeverity severity)
        {
            int n = 0;
            for (int i = 0; i < _items.Count; i++) if (_items[i].Severity == severity) n++;
            return n;
        }

        public void Write(SqJsonWriter w, string key)
        {
            _items.Sort((a, b) => ((int)b.Severity).CompareTo((int)a.Severity));

            w.BeginArray(key);
            for (int i = 0; i < _items.Count; i++) _items[i].Write(w);
            w.EndArray();
        }
    }
}

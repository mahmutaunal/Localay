using System;
using System.Collections.Generic;
using System.Linq;

namespace AlpWareStudio.Localay;

// Evaluation only: this policy never saves preferences or starts/downloads a model.
internal sealed record LocalModelEvidence(string Candidate, string ProfileIdentity, string ReviewedProfileIdentity,
    string ComparisonSet, string Origin, bool QualityReviewed, int CriticalErrors, bool NoQualityRegression,
    double WarmEndToEndP95Ms, double Coverage, double CpuMsPerFrame, double GpuMsPerFrame, long PeakWorkingSetBytes);
internal sealed record LocalModelVerdict(string? Candidate, string Reason);

internal static class LocalModelDecision
{
    internal static string? Rejection(LocalModelEvidence evidence)
    {
        if (evidence.Candidate is not ("marian-en-tr" or "translategemma-4b-en-tr")) return "unknown-candidate";
        if (string.IsNullOrWhiteSpace(evidence.ProfileIdentity) || evidence.ProfileIdentity != evidence.ReviewedProfileIdentity ||
            string.IsNullOrWhiteSpace(evidence.ComparisonSet)) return "identity-or-comparison-missing";
        string prefix = evidence.Candidate == "marian-en-tr" ? "marian-en-tr-v1:" : "translategemma-en-tr-v1:";
        if (!evidence.ProfileIdentity.StartsWith(prefix, StringComparison.Ordinal) ||
            evidence.ProfileIdentity.Length != prefix.Length + 64 || !evidence.ProfileIdentity[prefix.Length..].All(Uri.IsHexDigit))
            return "unpinned-profile";
        if (evidence.Origin != "observed-end-to-end") return "unmeasured-or-synthetic";
        if (!evidence.QualityReviewed || evidence.CriticalErrors != 0 || !evidence.NoQualityRegression) return "quality-gate";
        if (!double.IsFinite(evidence.WarmEndToEndP95Ms) || evidence.WarmEndToEndP95Ms <= 0 || evidence.WarmEndToEndP95Ms >= 100 ||
            evidence.Coverage != 1) return "latency-or-coverage-gate";
        if (!double.IsFinite(evidence.CpuMsPerFrame) || evidence.CpuMsPerFrame < 0 ||
            !double.IsFinite(evidence.GpuMsPerFrame) || evidence.GpuMsPerFrame < 0 || evidence.PeakWorkingSetBytes <= 0)
            return "resource-evidence-missing";
        return null;
    }

    internal static LocalModelVerdict Choose(IReadOnlyList<LocalModelEvidence> evidence)
    {
        if (evidence.GroupBy(e => e.Candidate).Any(g => g.Count() != 1)) return new(null, "duplicate-candidate-evidence");
        var passed = evidence.Where(e => Rejection(e) == null).OrderBy(e => e.WarmEndToEndP95Ms).ToArray();
        if (passed.Length == 0) return new(null, "no-candidate-passed");
        if (passed.Select(e => e.ComparisonSet).Distinct(StringComparer.Ordinal).Count() != 1)
            return new(null, "incomparable-evidence");
        if (passed.Length == 1) return new(passed[0].Candidate, "quality-and-latency-passed");
        var fastest = passed[0];
        var other = passed[1];
        if (other.WarmEndToEndP95Ms > fastest.WarmEndToEndP95Ms * 1.10)
            return new(fastest.Candidate, "lower-p95");
        bool LessOrEqual(LocalModelEvidence a, LocalModelEvidence b) => a.CpuMsPerFrame <= b.CpuMsPerFrame &&
            a.GpuMsPerFrame <= b.GpuMsPerFrame && a.PeakWorkingSetBytes <= b.PeakWorkingSetBytes;
        if (LessOrEqual(fastest, other)) return new(fastest.Candidate, "close-latency-lower-resources");
        if (LessOrEqual(other, fastest)) return new(other.Candidate, "close-latency-lower-resources");
        return new(null, "resource-tradeoff-needs-review");
    }
}

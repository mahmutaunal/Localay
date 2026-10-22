using System;
using System.Collections.Generic;
using System.Linq;

namespace AlpWareStudio.Localay;

// Reuse requires exact source text. Repeated labels need a motion supported by
// at least two unique anchors and an unambiguous geometric match in both directions.
internal static class OcrBlockMatcher
{
    internal static int[] Match(IReadOnlyList<OcrSceneBlock> previous, IReadOnlyList<OcrSceneBlock> current)
    {
        var result = Enumerable.Repeat(-1, current.Count).ToArray();
        var oldGroups = Enumerable.Range(0, previous.Count).GroupBy(i => previous[i].Text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var newGroups = Enumerable.Range(0, current.Count).GroupBy(i => current[i].Text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var anchors = new List<(double X, double Y)>();
        foreach (var (text, targets) in newGroups)
        {
            if (targets.Length != 1 || !oldGroups.TryGetValue(text, out var sources) || sources.Length != 1) continue;
            int from = sources[0], to = targets[0];
            result[to] = from;
            if (SameSize(previous[from], current[to]))
                anchors.Add((current[to].X - previous[from].X, current[to].Y - previous[from].Y));
        }
        var motions = anchors.Where(a => anchors.Count(b => Near(a.X, b.X) && Near(a.Y, b.Y)) >= 2).ToArray();
        foreach (var (text, targets) in newGroups)
        {
            if (!oldGroups.TryGetValue(text, out var sources) || (targets.Length == 1 && sources.Length == 1)) continue;
            bool Candidate(int from, int to) => SameSize(previous[from], current[to]) && motions.Any(m =>
                Near(previous[from].X + m.X, current[to].X) && Near(previous[from].Y + m.Y, current[to].Y));
            foreach (int to in targets)
            {
                int[] candidates = sources.Where(from => Candidate(from, to)).ToArray();
                if (candidates.Length == 1 && targets.Count(t => Candidate(candidates[0], t)) == 1)
                    result[to] = candidates[0];
            }
        }
        return result;
    }

    private static bool Near(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 2;
    private static bool SameSize(OcrSceneBlock a, OcrSceneBlock b) => Near(a.Width, b.Width) && Near(a.Height, b.Height);
}

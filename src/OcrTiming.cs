using System;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// Existing stage messages, tagged with the originating async operation when diagnostics
    /// are enabled. Durations and coverage live in PipelineTiming, not global stopwatches.
    /// </summary>
    public static class OcrTiming
    {

        // A captured frame was handed to the OCR engine (not logged: this happens every cycle)
        public static void MarkCaptureSent() => PipelineDiagnostics.Current?.Mark(PipelineStage.OcrStarted);

        // Stage text is legacy console content; the numeric diagnostic report contains no text.
        public static void NewTextDetected(string detail)
        {
            Log("1. New text from OCR", detail);
        }

        public static void Log(string stage, string detail = "")
        {
            var trace = PipelineDiagnostics.Current;
            string identity = trace == null ? "" : $" job={trace.Id}";
            Console.WriteLine($"[OCR timing {DateTime.Now:HH:mm:ss.fff}{identity}] {stage}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        public static string Preview(string text, int max = 40) => AudioTiming.Preview(text, max);
    }
}

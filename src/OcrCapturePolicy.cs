using System;

namespace AlpWareStudio.Localay;

// Capture observation remains active independently. These decisions only control
// repeating OCR or copying cached pixels; they never authorize displaying a result.
internal static class OcrCapturePolicy
{
    private static readonly TimeSpan RetryCheck = TimeSpan.FromMilliseconds(250);

    internal static bool ShouldRecognize(bool sameFrame, bool pendingSettlement, bool translating,
        bool resultSettled, TimeSpan sinceOcr, TimeSpan periodicCheck)
    {
        if (pendingSettlement) return false;
        if (!sameFrame) return true;
        if (translating) return false;
        return sinceOcr >= (resultSettled ? periodicCheck : RetryCheck);
    }

    internal static bool NeedsCachedFrame(bool ocrBusy, bool pendingSettlement, bool confirmationDue,
        bool translating, bool resultSettled, TimeSpan sinceOcr, TimeSpan periodicCheck)
    {
        if (ocrBusy) return false;
        if (pendingSettlement) return confirmationDue;
        if (translating) return false;
        return sinceOcr >= (resultSettled ? periodicCheck : RetryCheck);
    }
}

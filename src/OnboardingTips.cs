namespace AlpWareStudio.Localay
{
    /// <summary>
    /// Contextual hints shown when OCR finds no text or translation keeps failing. Also shows a one-time tip after the first success.
    /// </summary>
    public static class OnboardingTips
    {
        private const string FirstTranslationTipId = "first_translation";

        // With auto OCR, frames come in continuously, so wait for several empty frames before suggesting a fix
        private const int EmptyFramesBeforeNoTextTip = 10;
        // Show one classified failure per streak; background retries do not produce more tips.
        private const int FailuresBeforeTranslationTip = 1;

        private const int TipDurationMs = 12000;

        private static readonly object _lock = new object();
        private static int _emptyFramesThisSession;
        private static bool _textSeenThisSession;
        private static bool _noTextTipShownThisSession;
        private static int _consecutiveTranslationFailures;
        private static bool _translationFailureTipShown;

        /// <summary>
        /// Call when the user presses Start.
        /// </summary>
        public static void OnTranslationStarted()
        {
            lock (_lock)
            {
                _consecutiveTranslationFailures = 0;
                _translationFailureTipShown = false;
                _emptyFramesThisSession = 0;
                _textSeenThisSession = false;
                _noTextTipShownThisSession = false;
            }
        }

        /// <summary>
        /// Call for every processed OCR frame.
        /// </summary>
        public static void OnOcrResult(bool hasText)
        {
            lock (_lock)
            {
                if (hasText)
                {
                    _textSeenThisSession = true;
                    return;
                }

                // Empty frames are normal once text has been found (e.g. between dialogue lines)
                if (_textSeenThisSession || _noTextTipShownThisSession)
                    return;

                _emptyFramesThisSession++;
                int threshold = ConfigManager.Instance.IsAutoOCREnabled() ? EmptyFramesBeforeNoTextTip : 1;
                if (_emptyFramesThisSession < threshold)
                    return;

                _noTextTipShownThisSession = true;
            }

            Show("Tip_NoText_Title", S("Flow_GameNoText"));
        }

        /// <summary>
        /// Call after each OCR translation attempt.
        /// </summary>
        internal static void OnTranslationResult(string service, bool succeeded, TranslationFailure? failure = null, System.Threading.CancellationToken cancellation = default)
        {
            bool showFailureTip = false;
            lock (_lock)
            {
                if (succeeded)
                {
                    _consecutiveTranslationFailures = 0;
                    _translationFailureTipShown = false;
                }
                else
                {
                    _consecutiveTranslationFailures++;
                    if (_consecutiveTranslationFailures >= FailuresBeforeTranslationTip && !_translationFailureTipShown)
                    {
                        _translationFailureTipShown = true;
                        showFailureTip = true;
                    }
                }
            }

            if (showFailureTip)
            {
                Show("Tip_TranslationFailed_Title", UiText.Service(service) + ": " + S((failure ?? new(TranslationFailureKind.InvalidResponse)).MessageKey), cancellation);
            }

            if (succeeded)
            {
                var config = ConfigManager.Instance;
                ShowOnce(FirstTranslationTipId, "Tip_FirstTranslation_Title",
                    Format("Flow_GameSuccess", config.GetHotKey("Start/Stop"), S("Btn_Settings")));
            }
        }

        private static void ShowOnce(string tipId, string titleKey, string message)
        {
            if (ConfigManager.Instance.IsOnboardingTipShown(tipId))
                return;

            ConfigManager.Instance.MarkOnboardingTipShown(tipId);
            Show(titleKey, message);
        }

        private static void Show(string titleKey, string message, System.Threading.CancellationToken cancellation = default)
        {
            // Callers run on OCR/translation threads; the notification must be created on the UI thread
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!cancellation.IsCancellationRequested)
                    MainWindow.Instance.ShowFastNotification(S(titleKey), message, TipDurationMs);
            });
        }

        private static string S(string key) => LocalizationManager.Instance.Strings[key];

        private static string Format(string key, params object[] args) => string.Format(S(key), args);
    }
}

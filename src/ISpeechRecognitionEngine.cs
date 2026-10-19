using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// Abstraction for a speech-recognition engine (Whisper.net, FunASR/SenseVoice via sherpa-onnx, ...).
    /// The audio capture/VAD/buffering pipeline lives in localWhisperService; an engine only
    /// receives 16 kHz mono float samples and returns recognized text segments.
    /// </summary>
    public interface ISpeechRecognitionEngine : IDisposable
    {
        /// <summary>
        /// Load the model and prepare the engine. Called once on StartServiceAsync,
        /// before audio processing begins. Throw on failure — caller handles fallback/stop.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Recognize speech from 16 kHz mono float samples.
        /// Returns zero or more text segments (Whisper may yield multiple segments per call;
        /// SenseVoice returns a single utterance). Filtering/dedup is done by the caller.
        /// </summary>
        Task<IReadOnlyList<string>> RecognizeAsync(float[] samples, CancellationToken token);
    }
}

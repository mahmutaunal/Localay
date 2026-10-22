using System;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// Factory class for creating translation service instances based on configuration
    /// </summary>
    public static class TranslationServiceFactory
    {
        /// <summary>
        /// Create a translation service instance based on the current configuration
        /// </summary>
        /// <returns>An implementation of ITranslationService</returns>
        public static ITranslationService CreateService()
        {
            string currentService = ConfigManager.Instance.GetCurrentTranslationService();
            
            return currentService switch
            {
                "Gemini" => new GeminiTranslationService(),
                "Ollama" => new OllamaTranslationService(),
                MarianTranslationService.ServiceName => new MarianTranslationService(),
                "LM Studio" => new LMStudioTranslationService(),
                "ChatGPT" => new ChatGptTranslationService(),
                "Custom API" => new CustomApiTranslationService(),
                "Google Translate" => new GoogleTranslateService(),
                "Yandex" => new YandexTranslationService(),
                "Mistral" => new MistralTranslationService(),
                "Groq" => new GroqTranslationService(),
                "Microsoft" => new MicrosoftLegacyTranslationService(),
                _ => new GoogleTranslateService() // Zero-cost fallback; no API key required
            };
        }
        
        /// <summary>
        /// Create a specific translation service by name
        /// </summary>
        /// <param name="serviceName">Name of the service (Gemini, Ollama, ChatGPT, or Google Translate)</param>
        /// <returns>An implementation of ITranslationService</returns>
        public static ITranslationService CreateService(string serviceName)
        {
            return serviceName switch
            {
                "Gemini" => new GeminiTranslationService(),
                "Ollama" => new OllamaTranslationService(),
                MarianTranslationService.ServiceName => new MarianTranslationService(),
                "LM Studio" => new LMStudioTranslationService(),
                "ChatGPT" => new ChatGptTranslationService(),
                "Custom API" => new CustomApiTranslationService(),
                "Google Translate" => new GoogleTranslateService(),
                "Yandex" => new YandexTranslationService(),
                "Mistral" => new MistralTranslationService(),
                "Groq" => new GroqTranslationService(),
                "Microsoft" => new MicrosoftLegacyTranslationService(),
                _ => throw new ArgumentException($"Unknown translation service: {serviceName}")
            };
        }
    }
}

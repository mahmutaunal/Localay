#include <ctranslate2/translator.h>
#include <sentencepiece_processor.h>
#include "localay_marian.h"
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
struct Session {
  sentencepiece::SentencePieceProcessor source, target;
  std::unique_ptr<ctranslate2::Translator> translator;
};
void error_text(char* buffer, int capacity, const char* text) noexcept {
  if (buffer && capacity > 0) std::snprintf(buffer, static_cast<size_t>(capacity), "%s", text);
}
void release(char** values, int count) noexcept {
  if (!values) return;
  for (int i = 0; i < count; ++i) delete[] values[i];
  delete[] values;
}
void require(bool condition, const char* message) {
  if (!condition) throw std::runtime_error(message);
}
}

LOCALAY_API int localay_marian_open_v1(const char* model, const char* source,
    const char* target, int threads, void** context, char* error, int capacity) noexcept {
  if (context) *context = nullptr;
  try {
    require(context && model && source && target && threads >= 1 && threads <= 4, "Invalid initialization.");
    auto session = std::make_unique<Session>();
    require(session->source.Load(source).ok(), "Cannot load source tokenizer.");
    require(session->target.Load(target).ok(), "Cannot load target tokenizer.");
    ctranslate2::ReplicaPoolConfig pool;
    pool.num_threads_per_replica = static_cast<size_t>(threads);
    pool.max_queued_batches = 1;
    session->translator = std::make_unique<ctranslate2::Translator>(model,
        ctranslate2::Device::CPU, ctranslate2::ComputeType::INT8,
        std::vector<int>{0}, false, pool);
    *context = session.release();
    return 0;
  } catch (const std::exception& ex) { error_text(error, capacity, ex.what()); }
    catch (...) { error_text(error, capacity, "Native initialization failed."); }
  return 1;
}

LOCALAY_API int localay_marian_translate_v1(void* context, const char* const* input,
    int count, char*** output, char* error, int capacity) noexcept {
  if (output) *output = nullptr;
  char** values = nullptr;
  try {
    require(context && input && output && count > 0 && count <= 15, "Invalid translation batch.");
    auto& session = *static_cast<Session*>(context);
    std::vector<std::vector<std::string>> tokens(static_cast<size_t>(count));
    for (int i = 0; i < count; ++i) {
      require(input[i] && std::strlen(input[i]) <= 16384, "Input exceeds byte budget.");
      require(session.source.Encode(input[i], &tokens[i]).ok(), "Source tokenization failed.");
      require(!tokens[i].empty() && tokens[i].size() <= 512, "Input exceeds token budget.");
      require(std::find(tokens[i].begin(), tokens[i].end(), "<unk>") == tokens[i].end(), "Unknown source token rejected.");
    }
    ctranslate2::TranslationOptions options;
    options.beam_size = 2;
    options.max_input_length = 0; // Truncation disabled; over-budget input was rejected above.
    options.max_decoding_length = 1024;
    options.return_end_token = true;
    options.end_token = std::string("</s>");
    auto results = session.translator->translate_batch(tokens, options, 15);
    require(results.size() == static_cast<size_t>(count), "Incorrect result count.");
    values = new char*[count]{};
    for (int i = 0; i < count; ++i) {
      require(results[i].hypotheses.size() == 1, "Missing translation hypothesis.");
      auto& pieces = results[i].hypotheses[0];
      require(!pieces.empty() && pieces.back() == "</s>", "Truncated translation rejected.");
      pieces.pop_back();
      require(std::find(pieces.begin(), pieces.end(), "<unk>") == pieces.end(), "Unknown target token rejected.");
      std::string translated;
      require(session.target.Decode(pieces, &translated).ok() && !translated.empty(), "Target decoding failed.");
      values[i] = new char[translated.size() + 1];
      std::memcpy(values[i], translated.c_str(), translated.size() + 1);
    }
    *output = values;
    return 0;
  } catch (const std::exception& ex) { error_text(error, capacity, ex.what()); }
    catch (...) { error_text(error, capacity, "Native inference failed."); }
  release(values, std::max(0, count));
  return 1;
}

LOCALAY_API void localay_marian_release_v1(char** output, int count) noexcept { release(output, count); }
LOCALAY_API void localay_marian_close_v1(void* context) noexcept { delete static_cast<Session*>(context); }

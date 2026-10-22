# Optional Marian package — source integration, not a distributed runtime

This directory is **not** built by Localay.csproj or the installer workflow. No native binary,
weights or tokenizer was downloaded, built or validated during P5. The normal installer
continues to use its existing providers. Selecting `Marian (local)` without a prepared
package produces a readiness failure; it does not install anything or call another provider.

The initial profile is English → Turkish screen translation only:

- Model: [Helsinki-NLP/opus-mt-tc-big-en-tr](https://huggingface.co/Helsinki-NLP/opus-mt-tc-big-en-tr).
- Backend: CTranslate2 **4.8.3**, CPU INT8, one replica, at most four intra-op threads.
- Tokenizers: SentencePiece **0.2.1**; distinct source.spm and target.spm from the **same model revision**.
- Decode: beam 2, maximum source 512 pieces, maximum output 1024 pieces with required EOS.
  Long input, incomplete output and unknown tokens are rejected, not silently shortened.
- No GPU backend is enabled in this integration; that requires a separately evaluated profile.
- Clipboard/legacy JSON calls and other language pairs are rejected by this profile.

## Packaging contract (maintainer work, not an end-user prerequisite)

Use already installed dependencies. CMake requires the exact CTranslate2 version; the
SentencePiece prefix must be from the pinned 0.2.1 build (the installed library has no
versioned CMake discovery here). No dependency fetch is configured. With compatible
Windows x64 C++ toolchains, consistent CRT settings and those dependencies, the standalone
configuration is:

```text
cmake -S native/Marian -B <separate-build-dir> -DCMAKE_PREFIX_PATH=<ct2-prefix> -DSENTENCEPIECE_ROOT=<spm-prefix>
cmake --build <separate-build-dir> --config Release
```

These commands were **not run**. Do not add them to the main installer workflow until
the optional binary is separately built and its ABI/tokenizer equivalence is verified.
No Python runtime is required by the C# application or this native bridge. Development-time
model conversion is described in the [CTranslate2 Marian/OPUS documentation](https://opennmt.net/CTranslate2/guides/opus_mt.html).
Use the converter appropriate to the downloaded format: a Hugging Face PyTorch export is
not a Marian `.npz` archive. Conversion, INT8 output and tokenizer normalization equivalence
remain unvalidated; model packaging cannot be declared complete from source integration alone.

Place the optional **prebuilt** package under the application directory:

```text
LocalModels/opus-en-tr/
  profile.json
  localay_marian.dll
  <all native runtime dependency DLLs>
  source.spm
  target.spm
  model/
    model.bin
    config.json
    <all converted vocabularies and metadata>
  <model/runtime/tokenizer licenses and attribution>
```

The manifest schema is below. Placeholder fields deliberately fail readiness. Never replace
them with invented hashes/revisions. The 40-hex source revision must refer to the actual
model snapshot used; every package file except profile.json must appear in `files`, including
native dependencies, vocabulary files and notices. Each value is its SHA-256 digest.

```json
{
  "schema": 1,
  "model": "Helsinki-NLP/opus-mt-tc-big-en-tr",
  "revision": "ACTUAL_40_HEX_SOURCE_REVISION",
  "runtime": "CTranslate2-4.8.3",
  "tokenizer": "SentencePiece-0.2.1",
  "compute": "cpu-int8",
  "source": "en",
  "target": "tr",
  "files": {
    "localay_marian.dll": "ACTUAL_SHA256",
    "source.spm": "ACTUAL_SHA256",
    "target.spm": "ACTUAL_SHA256",
    "model/model.bin": "ACTUAL_SHA256",
    "model/config.json": "ACTUAL_SHA256"
  }
}
```

Hashes are checked once on the worker before loading. Model construction/inference stays
off the UI thread. The full manifest hash, profile version and translation settings separate
cache entries. Replace an installed package only while Localay is closed; restart after
installation, a readiness failure or any update. The model remains resident until process
exit, avoiding repeated load costs. An active native call completes before its slot is released;
cancellation discards its result and prevents subsequent inference.

The model card declares CC-BY-4.0; preserve model attribution alongside the runtime/tokenizer
license notices. The bridge is application source, not permission to redistribute arbitrary
third-party builds. No optional binaries are added to the current installer by this change.

## Portable ABI check (P6)

The shared `localay_marian.h` declares the production C ABI. A dependency-free test double
in `tests/LifetimeChecks/native/abi_fixture.cpp` was compiled locally with the host C++
compiler. With `LOCALAY_MARIAN_ABI_FIXTURE` pointing to its output directory (library named
`localay_marian.dll`), LifetimeChecks runs 15 additional checks against the actual
`MarianRuntime`: UTF-8 array transfer, output ownership, error cleanup, cancellation,
partial initialization and idempotent disposal. No fixture is required by normal tests or CI.

This caught and fixed an unsupported .NET string-array marshaling declaration. Explicit
UTF-8 allocations are now freed on every exit. The fixture does not build or validate the
real CTranslate2 bridge, a Windows binary, tokenizer equivalence or model inference.

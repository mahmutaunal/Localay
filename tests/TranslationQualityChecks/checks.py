"""Offline corpus checks and review preparation. No network, inference or API keys."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re

CORPUS = Path(__file__).with_name('corpus.json')
TOKEN = re.compile(r'\{[^{}]+\}|\[[^\[\]]+\]')


def load_corpus():
    raw = CORPUS.read_bytes()
    corpus = json.loads(raw)
    cases = corpus['cases']
    assert corpus['status'] == 'unmeasured' and corpus['contextSent'] is False
    assert (corpus['sourceLanguage'], corpus['targetLanguage']) == ('en', 'tr')
    assert len(cases) == 60 and len({c['id'] for c in cases}) == 60
    criteria = corpus['acceptanceCriteria']
    assert (criteria['requiredOutputCount'], criteria['standaloneCaseCount'], criteria['contextProbeCount']) == (60, 50, 10)
    assert (criteria['maximumCriticalErrors'], criteria['maximumMajorErrors'], criteria['maximumProtectedTokenChanges']) == (0, 0, 0)
    assert criteria['minimumNaturalStandaloneCases'] == 45
    assert criteria['independentHumanReviewCompleted'] is False
    assert len({c['source'] for c in cases}) == 60
    assert Counter(c['category'] for c in cases) == dict.fromkeys(
        ['ui', 'instruction', 'dialogue', 'context', 'item', 'format'], 10)
    for case in cases:
        assert all(isinstance(case[k], str) and case[k].strip() for k in
                   ['id', 'source', 'meaning', 'criticalRisk', 'reviewContext'])
        assert type(case['contextDependent']) is bool
        assert case['contextDependent'] == (case['category'] == 'context')
        assert case['acceptableExamples'] and all(isinstance(s, str) and s.strip()
                                                  for s in case['acceptableExamples'])
        assert Counter(case['protectedTokens']) == Counter(TOKEN.findall(case['source']))
        assert all(Counter(TOKEN.findall(s)) == Counter(case['protectedTokens'])
                   for s in case['acceptableExamples'])
    return corpus, hashlib.sha256(raw).hexdigest()


def structural_flags(source, translation):
    if not isinstance(translation, str) or not translation.strip():
        return ['missing_output']
    flags = []
    if Counter(TOKEN.findall(source)) != Counter(TOKEN.findall(translation)):
        flags.append('protected_tokens_changed')
    # A warning, not a verdict: spelled-out numbers or local grouping may be valid.
    def digits(text):
        return Counter(str(int(n)) for n in re.findall(r'\d+', TOKEN.sub('', text)))
    if digits(source) != digits(translation):
        flags.append('numeric_form_needs_review')
    return flags


def pending_template(corpus, digest):
    return {
        'corpusSha256': digest,
        'provenance': {'kind': 'pending_engine_output', 'provider': 'Google Translate',
                       'mode': 'web', 'sourceLanguage': 'en', 'targetLanguage': 'tr',
                       'requestLayout': 'single'},
        'results': [{'id': c['id'], 'source': c['source'], 'translation': None}
                    for c in corpus['cases']]
    }


def prepare_review(corpus, digest, outputs):
    if outputs.get('corpusSha256') != digest:
        raise ValueError('Outputs belong to a different corpus revision.')
    provenance = outputs.get('provenance', {})
    expected = {'kind': 'engine_output', 'provider': 'Google Translate', 'mode': 'web',
                'sourceLanguage': 'en', 'targetLanguage': 'tr'}
    if any(provenance.get(k) != v for k, v in expected.items()):
        raise ValueError('Expected declared Google web engine outputs, not pending, reference or fixture data.')
    if provenance.get('requestLayout') not in ('single', 'batch15'):
        raise ValueError('Record single-block or batch-of-15 collection separately.')
    known = {c['id']: c for c in corpus['cases']}
    indexed = {}
    for result in outputs.get('results', []):
        case_id = result.get('id')
        if case_id not in known or case_id in indexed:
            raise ValueError('Unknown or duplicate output ID.')
        if result.get('source') != known[case_id]['source']:
            raise ValueError('Output source does not match its corpus ID.')
        value = result.get('translation')
        if value is not None and not isinstance(value, str):
            raise ValueError('Translation must be a string or null.')
        indexed[case_id] = value
    cases = []
    for case in corpus['cases']:
        value = indexed.get(case['id'])
        cases.append({**case, 'providedTranslation': value,
                      'structuralFlags': structural_flags(case['source'], value),
                      'semanticReview': None, 'naturalnessReview': None, 'reviewNotes': None})
    return {'corpusSha256': digest, 'status': 'pending_semantic_review',
            'acceptanceCriteria': corpus['acceptanceCriteria'],
            'provenance': provenance, 'provenanceIndependentlyVerified': False,
            'qualityScore': None, 'endToEndLatencyMeasured': False,
            'providedOutputCount': sum(isinstance(c['providedTranslation'], str) and
                                      bool(c['providedTranslation'].strip()) for c in cases),
            'cases': cases}


def regression_checks(corpus, digest):
    # These are deliberately synthetic verifier inputs, never reported as model outputs.
    assert structural_flags('Hello {player}', 'Merhaba {player}') == []
    assert 'protected_tokens_changed' in structural_flags('Hello {player}', 'Merhaba {oyuncu}')
    assert 'protected_tokens_changed' in structural_flags('{x} {x}', '{x}')
    assert 'protected_tokens_changed' in structural_flags('Play', 'Oyna {invented}')
    assert structural_flags('Quest {0}: {1}', 'Görev {0}: {1}') == []
    assert 'numeric_form_needs_review' in structural_flags('Get 3 keys', '2 anahtar al')
    assert structural_flags('Get 3 keys', 'Üç anahtar al') == ['numeric_form_needs_review']
    # Reversed meaning with intact digits demonstrates why structural success is not semantic acceptance.
    assert structural_flags('You need 3 keys, not 2.', '3 değil, 2 anahtar gerekiyor.') == []
    assert structural_flags('Play', None) == ['missing_output']
    template = pending_template(corpus, digest)
    for kind in ['pending_engine_output', 'reference', 'test_fixture']:
        template['provenance']['kind'] = kind
        try:
            prepare_review(corpus, digest, template)
        except ValueError:
            pass
        else:
            raise AssertionError('Non-engine data entered semantic review.')
    template['provenance']['kind'] = 'engine_output'
    template['results'] = []
    report = prepare_review(corpus, digest, template)
    assert report['qualityScore'] is None and report['providedOutputCount'] == 0
    assert all(c['semanticReview'] is None and c['structuralFlags'] == ['missing_output']
               for c in report['cases'])
    template_row = pending_template(corpus, digest)['results'][0]
    partial = prepare_review(corpus, digest, {**template, 'results': [
        {**template_row, 'translation': corpus['cases'][0]['acceptableExamples'][0]}]})
    assert partial['providedOutputCount'] == 1 and partial['qualityScore'] is None
    assert partial['status'] == 'pending_semantic_review'
    for invalid in [
        {**template, 'corpusSha256': 'different'},
        {**template, 'results': [{'id': 'unknown', 'source': 'Play'}]},
        {**template, 'results': [template_row, template_row]},
        {**template, 'results': [{**template_row, 'source': 'another block'}]},
    ]:
        try:
            prepare_review(corpus, digest, invalid)
        except ValueError:
            pass
        else:
            raise AssertionError('Mismatched output provenance or ownership was accepted.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--template', type=Path, help='Write an empty, explicitly pending engine-output template.')
    group.add_argument('--prepare-review', type=Path, help='Read separately collected engine outputs; make no calls.')
    parser.add_argument('--out', type=Path, help='New review file; existing files are never overwritten.')
    args = parser.parse_args()
    if bool(args.prepare_review) != bool(args.out):
        parser.error('--prepare-review and --out must be used together.')
    corpus, digest = load_corpus()
    regression_checks(corpus, digest)
    if args.template:
        with args.template.open('x', encoding='utf-8') as stream:
            json.dump(pending_template(corpus, digest), stream, ensure_ascii=False, indent=2)
    if args.prepare_review:
        report = prepare_review(corpus, digest, json.loads(args.prepare_review.read_text(encoding='utf-8')))
        with args.out.open('x', encoding='utf-8') as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
    print('PASS: 60 corpus cases, 6 categories, protected-token checks and review-integrity regressions.')
    print('50 standalone cases; 10 context-dependent probes. Corpus SHA256: ' + digest)
    print('This offline command does not evaluate engine quality; see separately recorded reviews. No network, inference, Windows or API billing.')


if __name__ == '__main__':
    main()

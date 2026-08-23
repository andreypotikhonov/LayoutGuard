# Russian broken-key pipeline V2 (Windows)

This directory contains the reproducible, Windows-only pipeline for restoring
letters omitted by broken physical keys. Runtime remains fully offline and does
not contain Python, PyTorch, OpenCorpora XML, or user text.

## Architecture

V2 separates the exact error channel from ranking:

`observed → exact candidates → vocabulary → frequency/context ranking → threshold + margin → replace/keep`

- `build_lexicon.py` merges every available OpenCorpora surface form, honest
  Hunspell heads, the existing frequency list, and `data/ru_supplemental.tsv`.
- `build_language_stats.py` streams real OpenCorpora sentences and builds
  unigram, bigram, and trigram counts.
- `export_runtime_resources.py` compiles 3.35M forms into a compact minimal
  acyclic automaton and hashes language statistics into sorted arrays.
- `build_broken_dataset.py` makes lemma-family-safe synthetic corruptions with a
  deterministic 70/25/5 prior for one/two/three missing presses.
- `evaluate_broken_keys.py` reports candidate recall, recovery, clean/collision/
  slang/name preservation, buckets, and latency against `metrics/baseline_v1.json`.
- `build_all.py` is the single entry point.

`BrokenKeyCandidateGenerator` never substitutes, transposes, or deletes working
letters. A returned word can differ from the observed token only by insertion
of configured broken-key characters. Correct lexicon, supplemental, corpus-high-
frequency, and custom words are preserved before ranking.

Names are a first-class `NAME` category. OpenCorpora contributes hundreds of
thousands of name/surname/patronymic forms. The manual layer guarantees common
forms such as `Андрей`, `Андрея`, `Андрею`, `Андреем`, and `Андрее`.

## Rebuild

The preferred build-only input is `pymorphy3-dicts-ru`, which is a compiled
OpenCorpora dictionary and exposes all surface forms. A raw OpenCorpora XML
dictionary is also supported.

```powershell
$env:PYTHONPATH = "path\\to\\pymorphy3-build-packages"
python Windows/ml/build_all.py `
  --pymorphy-dictionary path\\to\\pymorphy3_dicts_ru\\data `
  --opencorpora-corpus path\\to\\opencorpora_annot.xml
```

Without `--opencorpora-corpus`, the pipeline explicitly enters degraded
unigram-only mode. It never silently claims that n-gram context was built.

Generated corpora and intermediate TSV files remain under `Windows/ml/artifacts`
and are excluded from Git. Only these compact resources ship:

- `Resources/Models/ru_broken_lexicon.bin`
- `Resources/Models/ru_language_stats.bin`
- `Resources/Models/ru_ranker.json`

The old character GapModel is retained only as an auxiliary ranking feature
until controlled evaluation shows that removing it is strictly safer.

## Tests

`data/ru_torture.tsv` is human-readable and is never used for training. Core
checks cover its recovery, morphology, names, slang, technical tokens, collision
and historical regression cases. Property checks verify candidate completeness
and the exact deletion-channel invariant. Windows checks exercise real
`SendInput`, focused child fields, context extraction, layout switching, and the
keyboard hot path.

Source versions, licenses, paths, hashes, and counts are written to manifests.
See `Windows/THIRD_PARTY.md` and the shipped license notices.

## Legacy V1 comparison

The original reproducible comparison remains available:

```powershell
python Windows/ml/build_dataset.py
python Windows/ml/train_gap_model.py
python Windows/ml/evaluate_gap_model.py
python Windows/ml/predict_gap_model.py ривет пивет потести релизь
```

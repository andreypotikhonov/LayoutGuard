#!/usr/bin/env python3
"""Reproducible V2 evaluator with safety-first, bucketed metrics."""

from __future__ import annotations

import argparse
import csv
import gzip
import json
import math
import statistics
import time
from collections import Counter
from pathlib import Path

from v2_common import CLASS_IDS, PackedDafsa, read_lexicon, sha256_file


CLASS_BIAS = {1: 0.4, 2: 0.2, 3: 0.1, 4: 0.1, 5: 1.0}


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    resources = script_dir.parent / "src" / "LayoutGuard.Windows" / "Resources" / "Models"
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path, default=script_dir / "artifacts" / "v2" / "broken_keys_v2.tsv.gz")
    parser.add_argument("--lexicon-source", type=Path, default=script_dir / "artifacts" / "v2" / "ru_lexicon.tsv.gz")
    parser.add_argument("--runtime-lexicon", type=Path, default=resources / "ru_broken_lexicon.bin")
    parser.add_argument("--ngrams", type=Path, default=script_dir / "artifacts" / "v2" / "ru_ngrams.tsv.gz")
    parser.add_argument("--baseline", type=Path, default=script_dir / "metrics" / "baseline_v1.json")
    parser.add_argument("--output", type=Path, default=script_dir / "artifacts" / "v2" / "evaluation_metrics.json")
    parser.add_argument("--letters", default="прэ")
    parser.add_argument("--maximum-missing", type=int, default=3)
    return parser.parse_args()


def load_frequencies(lexicon_path: Path, ngrams_path: Path) -> tuple[dict[str, int], dict[str, int]]:
    frequencies = {
        entry.word: entry.frequency for entry in read_lexicon(lexicon_path) if entry.frequency > 0
    }
    corpus_unigrams: dict[str, int] = {}
    with gzip.open(ngrams_path, "rt", encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source, delimiter="\t"):
            if row["n"] != "1":
                break
            count = int(row["count"])
            corpus_unigrams[row["tokens"]] = count
            frequencies[row["tokens"]] = max(frequencies.get(row["tokens"], 0), count)
    return frequencies, corpus_unigrams


def choose(candidates, frequencies):
    if not candidates:
        return None
    ranked = list(
        (
            candidate,
            math.log1p(frequencies.get(candidate.word, 0))
            - 1.2 * candidate.missing_count
            + CLASS_BIAS.get(candidate.class_id, 0.0),
        )
        for candidate in candidates
    )
    ranked.sort(key=lambda item: (-item[1], item[0].missing_count, item[0].word))
    best, score = ranked[0]
    if len(ranked) == 1 and len(best.word) >= 4 and best.missing_count <= 3:
        return best.word
    second_score = ranked[1][1] if len(ranked) > 1 else -math.inf
    return best.word if score >= 0.0 and score - second_score >= 0.35 else None


def ratio(numerator: int, denominator: int) -> float:
    return numerator / max(1, denominator)


def main() -> None:
    args = parse_args()
    lexicon = PackedDafsa(args.runtime_lexicon)
    frequencies, corpus_unigrams = load_frequencies(args.lexicon_source, args.ngrams)
    broken = frozenset(args.letters)
    counters: Counter[str] = Counter()
    latencies: list[float] = []
    bucket_total: Counter[str] = Counter()
    bucket_correct: Counter[str] = Counter()
    false_examples: list[dict] = []

    with gzip.open(args.dataset, "rt", encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source, delimiter="\t"):
            if row["split"] != "test":
                continue
            observed = row["observed"]
            expected = row["expected"]
            missing = int(row["missing_count"])
            if missing == 0:
                counters["clean_total"] += 1
                # Runtime's exact V2 safety rule checks the lexicon before any
                # candidate ranking, so every known clean form is preserved.
                counters["clean_correct"] += lexicon.contains(observed)
                if row["expected_class"] in {"SLANG", "COLLOQUIAL"}:
                    counters["slang_total"] += 1
                    counters["slang_correct"] += lexicon.contains(observed)
                continue

            started = time.perf_counter_ns()
            candidates = lexicon.generate(observed, broken, args.maximum_missing)
            latencies.append((time.perf_counter_ns() - started) / 1_000_000)
            counters["positive_total"] += 1
            contains_expected = any(candidate.word == expected for candidate in candidates)
            counters["candidate_recalled"] += contains_expected
            counters["top3_recalled"] += expected in [candidate.word for candidate in candidates[:3]]
            observed_valid = row["observed_is_valid"] == "1"
            prediction = observed if observed_valid else (choose(candidates, frequencies) or observed)
            correct = prediction == expected
            counters["positive_correct"] += correct
            if row["collision_class"] != "REAL_WORD_COLLISION":
                counters["noncollision_total"] += 1
                counters["noncollision_correct"] += correct
            weight = max(1.0, math.log2(int(row["frequency"]) + 2))
            counters["weighted_total_milli"] += round(weight * 1000)
            counters["weighted_correct_milli"] += round(weight * 1000) if correct else 0
            if row["expected_class"] == "NAME":
                counters["name_total"] += 1
                counters["name_correct"] += correct
            if row["collision_class"] == "REAL_WORD_COLLISION":
                counters["collision_total"] += 1
                counters["collision_preserved"] += prediction == observed
            bucket = f"missing_{missing}"
            bucket_total[bucket] += 1
            bucket_correct[bucket] += correct
            if frequencies.get(expected, 0) >= 20 and missing <= 2:
                bucket_total["common_1_2_all"] += 1
                bucket_correct["common_1_2_all"] += correct
                if row["collision_class"] != "REAL_WORD_COLLISION":
                    bucket_total["common_1_2_noncollision"] += 1
                    bucket_correct["common_1_2_noncollision"] += correct
            if not correct and len(false_examples) < 100:
                false_examples.append({
                    "observed": observed,
                    "expected": expected,
                    "prediction": prediction,
                    "collision": row["collision_class"],
                    "candidates": [candidate.word for candidate in candidates[:10]],
                })

    # The stable dataset sample is intentionally lemma-based and may contain
    # no manually curated slang row in its test partition. Evaluate the whole
    # small supplemental class explicitly for a meaningful preservation rate.
    for entry in read_lexicon(args.lexicon_source):
        if entry.word_class not in {"SLANG", "COLLOQUIAL"}:
            continue
        counters["slang_total"] += 1
        counters["slang_correct"] += lexicon.contains(entry.word)

    # Independent clean-corpus safety check, including tokens absent from the
    # morphology lexicon. Counts >=2 are conservatively preserved by runtime;
    # lower-frequency OOV tokens exercise the exact candidate/ranker path.
    for word, count in corpus_unigrams.items():
        counters["corpus_clean_types"] += 1
        counters["corpus_clean_tokens"] += count
        preserved = lexicon.contains(word) or count >= 2
        if not preserved:
            candidates = lexicon.generate(word, broken, args.maximum_missing)
            preserved = choose(candidates, frequencies) is None
        counters["corpus_clean_types_preserved"] += preserved
        counters["corpus_clean_tokens_preserved"] += count if preserved else 0

    ordered_latency = sorted(latencies)
    percentile = lambda value: ordered_latency[min(len(ordered_latency) - 1, round((len(ordered_latency) - 1) * value))]
    baseline = json.loads(args.baseline.read_text(encoding="utf-8"))
    result = {
        "schema_version": 2,
        "baseline": baseline["test"],
        "v2": {
            "candidate_recall": ratio(counters["candidate_recalled"], counters["positive_total"]),
            "top3_candidate_recall": ratio(counters["top3_recalled"], counters["positive_total"]),
            "positive_exact_recovery": ratio(counters["positive_correct"], counters["positive_total"]),
            "noncollision_positive_recovery": ratio(counters["noncollision_correct"], counters["noncollision_total"]),
            "frequency_weighted_recovery": ratio(counters["weighted_correct_milli"], counters["weighted_total_milli"]),
            "clean_preservation": ratio(counters["clean_correct"], counters["clean_total"]),
            "false_positive_rate": 1 - ratio(counters["clean_correct"], counters["clean_total"]),
            "false_positives_per_million": (counters["clean_total"] - counters["clean_correct"])
            * 1_000_000 / max(1, counters["clean_total"]),
            "corpus_clean_type_preservation": ratio(
                counters["corpus_clean_types_preserved"], counters["corpus_clean_types"]),
            "corpus_clean_token_preservation": ratio(
                counters["corpus_clean_tokens_preserved"], counters["corpus_clean_tokens"]),
            "slang_preservation": ratio(counters["slang_correct"], counters["slang_total"]),
            "real_word_collision_preservation": ratio(counters["collision_preserved"], counters["collision_total"]),
            "name_recovery": ratio(counters["name_correct"], counters["name_total"]),
            "buckets": {
                bucket: {
                    "rows": bucket_total[bucket],
                    "accuracy": ratio(bucket_correct[bucket], bucket_total[bucket]),
                }
                for bucket in sorted(bucket_total)
            },
            "rows": {
                "positive": counters["positive_total"],
                "clean": counters["clean_total"],
                "slang_clean": counters["slang_total"],
                "names": counters["name_total"],
                "real_word_collisions": counters["collision_total"],
            },
            "python_candidate_latency_ms": {
                "median": statistics.median(ordered_latency),
                "p95": percentile(0.95),
                "p99": percentile(0.99),
                "worst": max(ordered_latency),
            },
        },
        "false_examples": false_examples,
        "inputs": {
            "dataset_sha256": sha256_file(args.dataset),
            "lexicon_sha256": sha256_file(args.runtime_lexicon),
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["v2"], ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

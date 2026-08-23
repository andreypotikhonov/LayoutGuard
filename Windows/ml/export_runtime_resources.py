#!/usr/bin/env python3
"""Export compact, dependency-free runtime resources for Windows."""

from __future__ import annotations

import argparse
import csv
import gzip
import json
from pathlib import Path

from v2_common import (
    CLASS_IDS,
    MinimalDafsaBuilder,
    read_lexicon,
    sha256_file,
    write_hashed_stats,
)


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    resources = script_dir.parent / "src" / "LayoutGuard.Windows" / "Resources" / "Models"
    parser = argparse.ArgumentParser()
    parser.add_argument("--lexicon", type=Path, default=script_dir / "artifacts" / "v2" / "ru_lexicon.tsv.gz")
    parser.add_argument("--ngrams", type=Path, default=script_dir / "artifacts" / "v2" / "ru_ngrams.tsv.gz")
    parser.add_argument("--lexicon-output", type=Path, default=resources / "ru_broken_lexicon.bin")
    parser.add_argument("--stats-output", type=Path, default=resources / "ru_language_stats.bin")
    parser.add_argument("--ranker-output", type=Path, default=resources / "ru_ranker.json")
    parser.add_argument("--manifest", type=Path, default=script_dir / "artifacts" / "v2" / "runtime_manifest.json")
    return parser.parse_args()


def load_ngrams(path: Path):
    unigrams: list[tuple[str, int]] = []
    bigrams: list[tuple[tuple[str, str], int]] = []
    trigrams: list[tuple[tuple[str, str, str], int]] = []
    if not path.exists():
        return unigrams, bigrams, trigrams
    with gzip.open(path, "rt", encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source, delimiter="\t"):
            count = int(row["count"])
            tokens = tuple(row["tokens"].split("\0"))
            if row["n"] == "1":
                unigrams.append((tokens[0], count))
            elif row["n"] == "2" and len(tokens) == 2:
                bigrams.append((tokens, count))
            elif row["n"] == "3" and len(tokens) == 3:
                trigrams.append((tokens, count))
    return unigrams, bigrams, trigrams


def main() -> None:
    args = parse_args()
    builder = MinimalDafsaBuilder()
    previous = None
    merged_class = 0
    entries = read_lexicon(args.lexicon)
    for entry in entries:
        class_id = CLASS_IDS.get(entry.word_class, CLASS_IDS["STANDARD"])
        if previous is None:
            previous = entry.word
            merged_class = class_id
        elif entry.word == previous:
            merged_class = max(merged_class, class_id)
        else:
            builder.add(previous, merged_class)
            previous = entry.word
            merged_class = class_id
    if previous is not None:
        builder.add(previous, merged_class)
    root = builder.finish()
    lexicon_metrics = builder.write(args.lexicon_output, root)

    unigrams, bigrams, trigrams = load_ngrams(args.ngrams)
    stats_metrics = write_hashed_stats(args.stats_output, unigrams, bigrams, trigrams)
    ranker = {
        "schema_version": 1,
        "unigram_weight": 1.0,
        "bigram_weight": 1.25,
        "trigram_weight": 1.75,
        "missing_letter_penalty": 1.2,
        "gap_model_match_bonus": 1.5,
        "class_bias": {
            "STANDARD": 0.4,
            "NAME": 0.2,
            "COLLOQUIAL": 0.1,
            "TECH": 0.1,
            "CUSTOM": 1.0
        },
        "minimum_score": 0.0,
        "minimum_margin": 0.35,
        "unique_candidate_minimum_length": 4,
        "real_word_policy": "preserve_without_exception"
    }
    args.ranker_output.parent.mkdir(parents=True, exist_ok=True)
    args.ranker_output.write_text(json.dumps(ranker, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

    manifest = {
        "schema_version": 2,
        "lexicon": {**lexicon_metrics, "sha256": sha256_file(args.lexicon_output)},
        "statistics": {**stats_metrics, "sha256": sha256_file(args.stats_output)},
        "ranker": {"bytes": args.ranker_output.stat().st_size, "sha256": sha256_file(args.ranker_output)},
        "inputs": {
            "lexicon_sha256": sha256_file(args.lexicon),
            "ngrams_sha256": sha256_file(args.ngrams) if args.ngrams.exists() else None,
        },
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

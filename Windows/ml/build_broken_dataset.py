#!/usr/bin/env python3
"""Build lemma-safe synthetic V2 examples from real Russian surface forms."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import random
from collections import Counter
from pathlib import Path

from v2_common import PackedDafsa, open_deterministic_gzip, read_lexicon, sha256_file, stable_bucket


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    resources = script_dir.parent / "src" / "LayoutGuard.Windows" / "Resources" / "Models"
    parser = argparse.ArgumentParser()
    parser.add_argument("--lexicon", type=Path, default=script_dir / "artifacts" / "v2" / "ru_lexicon.tsv.gz")
    parser.add_argument("--runtime-lexicon", type=Path, default=resources / "ru_broken_lexicon.bin")
    parser.add_argument("--output", type=Path, default=script_dir / "artifacts" / "v2" / "broken_keys_v2.tsv.gz")
    parser.add_argument("--manifest", type=Path, default=script_dir / "artifacts" / "v2" / "dataset_manifest.json")
    parser.add_argument("--letters", default="прэ")
    parser.add_argument("--maximum-missing", type=int, default=3)
    parser.add_argument("--sample-modulo", type=int, default=10,
                        help="keep one of N stable word buckets; 1 keeps every form")
    parser.add_argument("--seed", type=int, default=20260823)
    return parser.parse_args()


def split_for_lemma(lemma_id: str) -> str:
    bucket = stable_bucket(lemma_id)
    return "train" if bucket < 80 else "validation" if bucket < 90 else "test"


def corrupt(word: str, broken: frozenset[str], maximum_missing: int, seed: int):
    positions = [index for index, character in enumerate(word) if character in broken]
    if not positions:
        return None
    key = hashlib.sha256(f"{seed}\0{word}".encode("utf-8")).digest()
    probability = int.from_bytes(key[:4], "big") / 2**32
    requested = 1 if probability < 0.70 else 2 if probability < 0.95 else 3
    missing_count = min(requested, maximum_missing, len(positions))
    rng = random.Random(int.from_bytes(key[4:12], "big"))
    removed = set(rng.sample(positions, missing_count))
    observed = "".join(character for index, character in enumerate(word) if index not in removed)
    if len(observed) < 2 or observed == word:
        return None
    return observed, sorted(removed)


def main() -> None:
    args = parse_args()
    broken = frozenset(args.letters.lower())
    lexicon = PackedDafsa(args.runtime_lexicon)
    counts: Counter[str] = Counter()
    raw, compressed, target = open_deterministic_gzip(args.output)
    try:
        writer = csv.writer(target, delimiter="\t", lineterminator="\n")
        writer.writerow([
            "split", "language", "lemma_id", "observed", "expected", "broken_letters",
            "missing_count", "missing_positions", "frequency", "expected_source", "expected_class",
            "observed_is_valid", "candidate_count", "collision_class"
        ])
        for entry in read_lexicon(args.lexicon):
            if stable_bucket(entry.word, args.sample_modulo) != 0:
                continue
            split = split_for_lemma(entry.lemma_id)
            writer.writerow([
                split, "ru", entry.lemma_id, entry.word, entry.word, args.letters, 0, "",
                entry.frequency, entry.source, entry.word_class, 1, 0, "CLEAN"
            ])
            counts[f"{split}_clean"] += 1
            damaged = corrupt(entry.word, broken, args.maximum_missing, args.seed)
            if damaged is None:
                continue
            observed, positions = damaged
            candidates = lexicon.generate(observed, broken, args.maximum_missing)
            observed_valid = lexicon.contains(observed)
            collision = "REAL_WORD_COLLISION" if observed_valid else (
                "MULTI_CANDIDATE" if len(candidates) > 1 else "NON_WORD"
            )
            writer.writerow([
                split, "ru", entry.lemma_id, observed, entry.word, args.letters, len(positions),
                ",".join(map(str, positions)), entry.frequency, entry.source, entry.word_class,
                int(observed_valid), len(candidates), collision
            ])
            counts[f"{split}_positive"] += 1
            counts[f"missing_{len(positions)}"] += 1
            counts[collision] += 1
            if entry.word_class == "NAME":
                counts["name_positive"] += 1
    finally:
        target.close()
        compressed.close()
        raw.close()

    manifest = {
        "schema_version": 2,
        "seed": args.seed,
        "letters": args.letters,
        "maximum_missing": args.maximum_missing,
        "missing_prior": {"1": 0.70, "2": 0.25, "3": 0.05},
        "split_key": "sha256(lemma_id) modulo 100",
        "split": {"train": 80, "validation": 10, "test": 10},
        "sample_modulo": args.sample_modulo,
        "counts": dict(sorted(counts.items())),
        "dataset": args.output.name,
        "dataset_sha256": sha256_file(args.output),
        "lexicon_sha256": sha256_file(args.runtime_lexicon),
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

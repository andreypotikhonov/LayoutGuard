#!/usr/bin/env python3
"""Build conservative Russian unigram/bigram/trigram counts from real sentences."""

from __future__ import annotations

import argparse
import csv
import gzip
import io
import json
import re
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

from v2_common import open_deterministic_gzip, sha256_file


RUSSIAN_TOKEN = re.compile(r"^[а-яё]+$", re.IGNORECASE)


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--opencorpora-corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=script_dir / "artifacts" / "v2" / "ru_ngrams.tsv.gz")
    parser.add_argument("--manifest", type=Path, default=script_dir / "artifacts" / "v2" / "language_stats_manifest.json")
    parser.add_argument("--maximum-bigrams", type=int, default=200_000)
    parser.add_argument("--maximum-trigrams", type=int, default=200_000)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    unigrams: Counter[str] = Counter()
    bigrams: Counter[tuple[str, str]] = Counter()
    trigrams: Counter[tuple[str, str, str]] = Counter()
    sentences = tokens = 0

    for _, element in ET.iterparse(args.opencorpora_corpus, events=("end",)):
        if element.tag != "sentence":
            continue
        sentence = [
            token.attrib.get("text", "").lower()
            for token in element.findall("./tokens/token")
        ]
        sentence = [token for token in sentence if RUSSIAN_TOKEN.fullmatch(token)]
        if sentence:
            sentences += 1
            tokens += len(sentence)
            unigrams.update(sentence)
            bigrams.update(zip(sentence, sentence[1:]))
            trigrams.update(zip(sentence, sentence[1:], sentence[2:]))
        element.clear()

    selected_bigrams = sorted(bigrams.items(), key=lambda item: (-item[1], item[0]))[: args.maximum_bigrams]
    selected_trigrams = sorted(trigrams.items(), key=lambda item: (-item[1], item[0]))[: args.maximum_trigrams]
    raw, compressed, target = open_deterministic_gzip(args.output)
    try:
        writer = csv.writer(target, delimiter="\t", lineterminator="\n")
        writer.writerow(["n", "tokens", "count"])
        for word, count in sorted(unigrams.items()):
            writer.writerow([1, word, count])
        for words, count in selected_bigrams:
            writer.writerow([2, "\0".join(words), count])
        for words, count in selected_trigrams:
            writer.writerow([3, "\0".join(words), count])
    finally:
        target.close()
        compressed.close()
        raw.close()

    manifest = {
        "schema_version": 1,
        "sentences": sentences,
        "tokens": tokens,
        "unique_unigrams": len(unigrams),
        "unique_bigrams_observed": len(bigrams),
        "unique_trigrams_observed": len(trigrams),
        "exported_bigrams": len(selected_bigrams),
        "exported_trigrams": len(selected_trigrams),
        "output": args.output.name,
        "output_sha256": sha256_file(args.output),
        "source": {
            "name": "OpenCorpora annotated corpus",
            "path": str(args.opencorpora_corpus),
            "license": "CC BY-SA 3.0",
            "sha256": sha256_file(args.opencorpora_corpus),
        },
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

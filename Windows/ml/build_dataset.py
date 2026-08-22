#!/usr/bin/env python3
"""Build a reproducible Russian broken-key dataset from bundled resources.

Each row is a supervised example: the text a keyboard with the selected dead
keys would produce and the intended dictionary word. Splits are assigned from
a stable hash of the intended word, so an exact word cannot leak between train,
validation, and test.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import itertools
import io
import json
import math
import re
from collections import Counter
from pathlib import Path


RUSSIAN_WORD = re.compile(r"^[а-яё]+$")
BLOOM_BITS = 1 << 24
BLOOM_HASHES = 16
FNV_PRIME = 1099511628211
FNV_MASK = (1 << 64) - 1
FNV_SEED_1 = 14695981039346656037
FNV_SEED_2 = 7809847782465536322


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    resources = script_dir.parent / "src" / "LayoutGuard.Windows" / "Resources"
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--dictionary",
        type=Path,
        default=resources / "Dictionaries" / "ru_RU.dic",
    )
    parser.add_argument(
        "--frequency",
        type=Path,
        default=resources / "Frequencies" / "ru_50k.txt",
    )
    parser.add_argument("--output-dir", type=Path, default=script_dir / "artifacts")
    parser.add_argument("--letters", default="прэ")
    parser.add_argument("--maximum-missing", type=int, default=3)
    parser.add_argument("--minimum-length", type=int, default=3)
    parser.add_argument("--maximum-length", type=int, default=40)
    return parser.parse_args()


def load_dictionary(path: Path, minimum_length: int, maximum_length: int) -> set[str]:
    # The bundled LibreOffice Russian dictionary declares SET KOI8-R.
    lines = path.read_text(encoding="koi8-r").splitlines()
    words: set[str] = set()
    for line in lines[1:]:
        word = line.split("/", 1)[0].strip().lower()
        if minimum_length <= len(word) <= maximum_length and RUSSIAN_WORD.fullmatch(word):
            words.add(word)
    return words


def load_frequencies(path: Path) -> dict[str, int]:
    result: dict[str, int] = {}
    with path.open("r", encoding="utf-8") as source:
        for line in source:
            word, separator, count = line.rstrip().rpartition(" ")
            if separator and count.isdigit():
                result[word.lower()] = int(count)
    return result


def split_for(word: str) -> str:
    bucket = int.from_bytes(hashlib.sha256(word.encode("utf-8")).digest()[:8], "big") % 100
    if bucket < 80:
        return "train"
    if bucket < 90:
        return "validation"
    return "test"


def fnv1a_characters(word: str, seed: int) -> int:
    value = seed
    for character in word:
        value ^= ord(character)
        value = (value * FNV_PRIME) & FNV_MASK
    return value


def write_vocabulary_bloom(words: set[str], path: Path) -> None:
    bits = bytearray(BLOOM_BITS // 8)
    for word in words:
        first = fnv1a_characters(word, FNV_SEED_1)
        second = fnv1a_characters(word, FNV_SEED_2) | 1
        for index in range(BLOOM_HASHES):
            position = (first + index * second) % BLOOM_BITS
            bits[position >> 3] |= 1 << (position & 7)
    with path.open("wb") as target:
        target.write(b"LGBF")
        target.write((1).to_bytes(4, "little"))
        target.write(BLOOM_BITS.to_bytes(4, "little"))
        target.write(BLOOM_HASHES.to_bytes(4, "little"))
        target.write(bits)


def display_path(path: Path, base: Path) -> str:
    try:
        return path.resolve().relative_to(base.resolve()).as_posix()
    except ValueError:
        return path.name


def examples_for(
    word: str,
    broken: frozenset[str],
    maximum_missing: int,
    frequency: int,
) -> list[tuple[str, str, int, int, int]]:
    # The keys may fail intermittently, so generate every unique choice of one
    # to three missing positions, not only the variant where every occurrence
    # disappears. This includes cases such as one working and one missed `р`.
    positions = [index for index, character in enumerate(word) if character in broken]
    examples = [(word, word, 0, 0, frequency)]
    seen = {word}
    for missing_count in range(1, min(maximum_missing, len(positions)) + 1):
        for deleted_positions in itertools.combinations(positions, missing_count):
            deleted = set(deleted_positions)
            observed = "".join(character for index, character in enumerate(word) if index not in deleted)
            if len(observed) < 2 or observed in seen:
                continue
            seen.add(observed)
            examples.append((observed, word, 1, missing_count, frequency))
    return examples


def main() -> None:
    args = parse_args()
    windows_directory = Path(__file__).resolve().parent.parent
    letters = "".join(dict.fromkeys(args.letters.lower()))
    if not letters or not RUSSIAN_WORD.fullmatch(letters):
        raise SystemExit("--letters must contain Russian letters only")
    if args.maximum_missing < 1:
        raise SystemExit("--maximum-missing must be positive")

    words = load_dictionary(args.dictionary, args.minimum_length, args.maximum_length)
    frequencies = load_frequencies(args.frequency)
    words.update(
        word for word in frequencies
        if args.minimum_length <= len(word) <= args.maximum_length and RUSSIAN_WORD.fullmatch(word)
    )

    output_dir = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)
    dataset_path = output_dir / "broken_keys_ru.tsv.gz"
    vocabulary_path = output_dir / "broken_key_vocabulary.bloom"
    write_vocabulary_bloom(words, vocabulary_path)
    broken = frozenset(letters)
    counts: Counter[str] = Counter()
    letter_counts: Counter[str] = Counter()
    weighted: Counter[str] = Counter()

    with dataset_path.open("wb") as raw_target, \
        gzip.GzipFile(filename="", mode="wb", fileobj=raw_target, mtime=0) as compressed_target, \
        io.TextIOWrapper(compressed_target, encoding="utf-8", newline="") as target:
        writer = csv.writer(target, delimiter="\t", lineterminator="\n")
        writer.writerow(
            ["split", "broken_letters", "observed", "expected", "is_correction", "missing_count", "frequency"]
        )
        for word in sorted(words):
            split = split_for(word)
            examples = examples_for(word, broken, args.maximum_missing, frequencies.get(word, 0))
            for observed, expected, is_correction, missing_count, frequency in examples:
                writer.writerow([split, letters, observed, expected, is_correction, missing_count, frequency])
                counts[f"{split}_rows"] += 1
                counts[f"{split}_{'positive' if is_correction else 'negative'}"] += 1
                weighted[split] += max(1, round(math.log2(frequency + 2)))
                if is_correction:
                    for letter in broken:
                        if word.count(letter) > observed.count(letter):
                            letter_counts[f"{split}_{letter}"] += 1

    manifest = {
        "schema_version": 1,
        "letters": letters,
        "maximum_missing": args.maximum_missing,
        "split": {"train": 80, "validation": 10, "test": 10},
        "split_key": "sha256(expected) modulo 100",
        "dictionary_words": len(words),
        "frequency_words": len(frequencies),
        "counts": dict(sorted(counts.items())),
        "positive_words_by_letter": dict(sorted(letter_counts.items())),
        "log_frequency_weight_by_split": dict(sorted(weighted.items())),
        "dataset": dataset_path.name,
        "dataset_sha256": hashlib.sha256(dataset_path.read_bytes()).hexdigest(),
        "vocabulary_bloom": vocabulary_path.name,
        "vocabulary_bloom_bits": BLOOM_BITS,
        "vocabulary_bloom_hashes": BLOOM_HASHES,
        "vocabulary_bloom_sha256": hashlib.sha256(vocabulary_path.read_bytes()).hexdigest(),
        "sources": [
            {
                "path": display_path(args.dictionary, windows_directory),
                "encoding": "KOI8-R",
                "license": "MPL-2.0",
            },
            {
                "path": display_path(args.frequency, windows_directory),
                "encoding": "UTF-8",
                "license": "CC BY-SA 4.0",
            },
        ],
    }
    (output_dir / "dataset_manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

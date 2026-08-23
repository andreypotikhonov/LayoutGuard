#!/usr/bin/env python3
"""Build the Russian surface-form lexicon used by broken-key V2.

The preferred input is a compiled pymorphy3/OpenCorpora dictionary because it
retains every surface form without requiring a home-grown Hunspell expander.
The raw OpenCorpora XML format is supported as an alternative.
"""

from __future__ import annotations

import argparse
import bz2
import csv
import gzip
import hashlib
import json
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator

from v2_common import is_russian_word, open_deterministic_gzip, sha256_file


@dataclass
class MutableEntry:
    lemma_id: str
    word_class: str
    frequency: int
    source: str


CLASS_PRIORITY = {
    "STANDARD": 0,
    "ABBREVIATION": 1,
    "TECH": 2,
    "COLLOQUIAL": 3,
    "SLANG": 4,
    "NAME": 5,
}


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    resources = script_dir.parent / "src" / "LayoutGuard.Windows" / "Resources"
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--pymorphy-dictionary", type=Path)
    source.add_argument("--opencorpora-dictionary", type=Path)
    parser.add_argument("--hunspell", type=Path, default=resources / "Dictionaries" / "ru_RU.dic")
    parser.add_argument("--frequency", type=Path, default=resources / "Frequencies" / "ru_50k.txt")
    parser.add_argument("--supplemental", type=Path, default=script_dir / "data" / "ru_supplemental.tsv")
    parser.add_argument("--output", type=Path, default=script_dir / "artifacts" / "v2" / "ru_lexicon.tsv.gz")
    parser.add_argument("--manifest", type=Path, default=script_dir / "artifacts" / "v2" / "lexicon_manifest.json")
    parser.add_argument("--minimum-length", type=int, default=2)
    parser.add_argument("--maximum-length", type=int, default=40)
    return parser.parse_args()


def lemma_key(normal_form: str, paradigm: str) -> str:
    return hashlib.sha256(f"{normal_form}\0{paradigm}".encode("utf-8")).hexdigest()[:16]


def classify_grammemes(grammemes: set[str]) -> str:
    if grammemes & {"Name", "Surn", "Patr"}:
        return "NAME"
    if "Abbr" in grammemes:
        return "ABBREVIATION"
    return "STANDARD"


def iter_pymorphy(path: Path) -> Iterator[tuple[str, str, str, str]]:
    try:
        from pymorphy3.opencorpora_dict.wrapper import Dictionary
    except ImportError as error:
        raise SystemExit(
            "pymorphy3 is required for --pymorphy-dictionary; install it in a build-only environment"
        ) from error
    dictionary = Dictionary(str(path))
    for word, tag, normal_form, paradigm_id, _ in dictionary.iter_known_words(""):
        yield word.lower(), normal_form.lower(), str(paradigm_id), classify_grammemes(set(tag.grammemes))


def open_xml(path: Path):
    if path.suffix == ".bz2":
        return bz2.open(path, "rb")
    if path.suffix == ".gz":
        return gzip.open(path, "rb")
    return path.open("rb")


def iter_opencorpora_xml(path: Path) -> Iterator[tuple[str, str, str, str]]:
    with open_xml(path) as source:
        for _, element in ET.iterparse(source, events=("end",)):
            if element.tag != "lemma":
                continue
            lemma_id = element.attrib.get("id", "")
            lemma_node = element.find("l")
            if lemma_node is None:
                element.clear()
                continue
            normal = lemma_node.attrib.get("t", "").lower()
            lemma_grammemes = {node.attrib.get("v", "") for node in lemma_node.findall("g")}
            for form in element.findall("f"):
                word = form.attrib.get("t", "").lower()
                grammemes = lemma_grammemes | {node.attrib.get("v", "") for node in form.findall("g")}
                yield word, normal, lemma_id, classify_grammemes(grammemes)
            element.clear()


def load_frequencies(path: Path) -> dict[str, int]:
    frequencies: dict[str, int] = {}
    with path.open("r", encoding="utf-8") as source:
        for line in source:
            word, separator, count = line.rstrip().rpartition(" ")
            if separator and count.isdigit():
                frequencies[word.lower()] = int(count)
    return frequencies


def merge(
    entries: dict[str, MutableEntry],
    word: str,
    lemma_id: str,
    word_class: str,
    frequency: int,
    source: str,
    minimum: int,
    maximum: int,
) -> None:
    if not is_russian_word(word, minimum, maximum):
        return
    existing = entries.get(word)
    if existing is None:
        entries[word] = MutableEntry(lemma_id, word_class, frequency, source)
        return
    existing.frequency = max(existing.frequency, frequency)
    if CLASS_PRIORITY.get(word_class, 0) > CLASS_PRIORITY.get(existing.word_class, 0):
        existing.word_class = word_class
        existing.lemma_id = lemma_id
    sources = set(existing.source.split("+"))
    if source not in sources:
        existing.source += "+" + source


def main() -> None:
    args = parse_args()
    frequencies = load_frequencies(args.frequency)
    entries: dict[str, MutableEntry] = {}
    counts = {"opencorpora_parses": 0, "hunspell_heads": 0, "frequency_words": 0, "supplemental": 0}

    iterator = (
        iter_pymorphy(args.pymorphy_dictionary)
        if args.pymorphy_dictionary
        else iter_opencorpora_xml(args.opencorpora_dictionary)
    )
    for word, normal, paradigm, word_class in iterator:
        counts["opencorpora_parses"] += 1
        merge(
            entries, word, lemma_key(normal, paradigm), word_class,
            frequencies.get(word, 0), "OpenCorpora", args.minimum_length, args.maximum_length
        )

    # Hunspell remains a runtime validator. Heads are also merged explicitly,
    # but we do not pretend that dropping flags expands all morphological forms.
    lines = args.hunspell.read_text(encoding="koi8-r").splitlines()
    for line in lines[1:]:
        word = line.split("/", 1)[0].strip().lower()
        if is_russian_word(word, args.minimum_length, args.maximum_length):
            counts["hunspell_heads"] += 1
            merge(entries, word, lemma_key(word, "hunspell"), "STANDARD", frequencies.get(word, 0),
                  "Hunspell-head", args.minimum_length, args.maximum_length)

    for word, frequency in frequencies.items():
        if is_russian_word(word, args.minimum_length, args.maximum_length):
            counts["frequency_words"] += 1
            merge(entries, word, lemma_key(word, "frequency"), "STANDARD", frequency,
                  "FrequencyWords", args.minimum_length, args.maximum_length)

    with args.supplemental.open("r", encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source, delimiter="\t"):
            word = row["word"].strip().lower()
            counts["supplemental"] += 1
            merge(entries, word, lemma_key(word, "supplemental"), row["class"], int(row["frequency"]),
                  row["source"], args.minimum_length, args.maximum_length)

    raw, compressed, target = open_deterministic_gzip(args.output)
    try:
        writer = csv.writer(target, delimiter="\t", lineterminator="\n")
        writer.writerow(["word", "lemma_id", "class", "frequency", "source"])
        for word in sorted(entries):
            entry = entries[word]
            writer.writerow([word, entry.lemma_id, entry.word_class, entry.frequency, entry.source])
    finally:
        target.close()
        compressed.close()
        raw.close()

    class_counts: dict[str, int] = {}
    source_counts: dict[str, int] = {}
    lemmas: set[str] = set()
    for entry in entries.values():
        class_counts[entry.word_class] = class_counts.get(entry.word_class, 0) + 1
        lemmas.add(entry.lemma_id)
        for source in entry.source.split("+"):
            source_counts[source] = source_counts.get(source, 0) + 1

    primary_source = args.pymorphy_dictionary or args.opencorpora_dictionary
    manifest = {
        "schema_version": 2,
        "forms": len(entries),
        "lemmas": len(lemmas),
        "classes": dict(sorted(class_counts.items())),
        "source_memberships": dict(sorted(source_counts.items())),
        "input_counts": counts,
        "contains_andrey_forms": {
            word: word in entries for word in ("андрей", "андрея", "андрею", "андреем", "андрее")
        },
        "output": args.output.name,
        "output_sha256": sha256_file(args.output),
        "sources": [
            {
                "name": "OpenCorpora morphological dictionary",
                "path": str(primary_source),
                "license": "CC BY-SA 3.0",
                "sha256": sha256_file(primary_source / "words.dawg")
                if primary_source.is_dir() else sha256_file(primary_source),
            },
            {
                "name": "LibreOffice Russian Hunspell",
                "path": str(args.hunspell),
                "license": "MPL-2.0",
                "sha256": sha256_file(args.hunspell),
            },
            {
                "name": "FrequencyWords Russian 50k",
                "path": str(args.frequency),
                "license": "CC BY-SA 4.0",
                "sha256": sha256_file(args.frequency),
            },
            {
                "name": "LayoutGuard conservative supplemental vocabulary",
                "path": str(args.supplemental),
                "license": "project license",
                "sha256": sha256_file(args.supplemental),
            },
        ],
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

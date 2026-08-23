#!/usr/bin/env python3
"""Shared deterministic formats and exact broken-key algorithms for V2."""

from __future__ import annotations

import csv
import gzip
import hashlib
import io
import struct
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Iterable, Iterator


LEXICON_MAGIC = b"LGV2"
LEXICON_VERSION = 2
STATS_MAGIC = b"LGST"
STATS_VERSION = 1
RUSSIAN_ALPHABET = frozenset("абвгдеёжзийклмнопрстуфхцчшщъыьэюя")

CLASS_IDS = {
    "STANDARD": 1,
    "NAME": 2,
    "COLLOQUIAL": 3,
    "SLANG": 3,
    "TECH": 4,
    "ABBREVIATION": 4,
    "CUSTOM": 5,
}


@dataclass(frozen=True)
class LexiconEntry:
    word: str
    lemma_id: str
    word_class: str
    frequency: int
    source: str


@dataclass(frozen=True)
class Candidate:
    word: str
    missing_count: int
    class_id: int


def stable_bucket(value: str, modulo: int = 100) -> int:
    return int.from_bytes(hashlib.sha256(value.encode("utf-8")).digest()[:8], "big") % modulo


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def is_russian_word(word: str, minimum: int = 2, maximum: int = 40) -> bool:
    return minimum <= len(word) <= maximum and all(character in RUSSIAN_ALPHABET for character in word)


def open_deterministic_gzip(path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = path.open("wb")
    compressed = gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0)
    text = io.TextIOWrapper(compressed, encoding="utf-8", newline="")
    return raw, compressed, text


def read_lexicon(path: Path) -> Iterator[LexiconEntry]:
    with gzip.open(path, "rt", encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source, delimiter="\t"):
            yield LexiconEntry(
                row["word"], row["lemma_id"], row["class"],
                int(row["frequency"]), row["source"]
            )


class MutableState:
    __slots__ = ("terminal_class", "children")

    def __init__(self) -> None:
        self.terminal_class = 0
        self.children: dict[str, MutableState | int] = {}


class MinimalDafsaBuilder:
    """Incremental minimal acyclic automaton for lexicographically sorted words."""

    def __init__(self) -> None:
        self.root = MutableState()
        self.previous = ""
        self.unchecked: list[tuple[MutableState, str, MutableState]] = []
        self.registry: dict[tuple[int, tuple[tuple[str, int], ...]], int] = {}
        self.states: list[tuple[int, tuple[tuple[str, int], ...]]] = []
        self.words = 0

    def add(self, word: str, terminal_class: int) -> None:
        if word < self.previous:
            raise ValueError("words must be added in lexicographic order")
        common = 0
        maximum = min(len(word), len(self.previous))
        while common < maximum and word[common] == self.previous[common]:
            common += 1
        self._minimize(common)
        node = self.root if common == 0 else self.unchecked[common - 1][2]
        for character in word[common:]:
            child = MutableState()
            node.children[character] = child
            self.unchecked.append((node, character, child))
            node = child
        if node.terminal_class == 0:
            self.words += 1
        node.terminal_class = max(node.terminal_class, terminal_class)
        self.previous = word

    def finish(self) -> int:
        self._minimize(0)
        return self._register(self.root)

    def _minimize(self, down_to: int) -> None:
        while len(self.unchecked) > down_to:
            parent, character, child = self.unchecked.pop()
            parent.children[character] = self._register(child)

    def _register(self, state: MutableState) -> int:
        transitions = tuple(sorted((char, int(target)) for char, target in state.children.items()))
        signature = (state.terminal_class, transitions)
        existing = self.registry.get(signature)
        if existing is not None:
            return existing
        state_id = len(self.states)
        self.states.append(signature)
        self.registry[signature] = state_id
        return state_id

    def write(self, path: Path, root_state: int) -> dict[str, int]:
        transition_count = sum(len(transitions) for _, transitions in self.states)
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("wb") as target:
            target.write(LEXICON_MAGIC)
            target.write(struct.pack("<IIIII", LEXICON_VERSION, len(self.states), transition_count, root_state, self.words))
            first_edge = 0
            for terminal_class, transitions in self.states:
                target.write(struct.pack("<IHBB", first_edge, len(transitions), terminal_class, 0))
                first_edge += len(transitions)
            for _, transitions in self.states:
                for character, child in transitions:
                    target.write(struct.pack("<IHH", child, ord(character), 0))
        return {
            "states": len(self.states),
            "transitions": transition_count,
            "words": self.words,
            "bytes": path.stat().st_size,
        }


class PackedDafsa:
    def __init__(self, path: Path) -> None:
        with path.open("rb") as source:
            if source.read(4) != LEXICON_MAGIC:
                raise ValueError("invalid V2 lexicon magic")
            version, state_count, transition_count, self.root, self.word_count = struct.unpack("<IIIII", source.read(20))
            if version != LEXICON_VERSION:
                raise ValueError("unsupported V2 lexicon version")
            self.states = [struct.unpack("<IHBB", source.read(8)) for _ in range(state_count)]
            self.edges = [struct.unpack("<IHH", source.read(8)) for _ in range(transition_count)]

    def transition(self, state: int, character: str) -> int | None:
        first, count, _, _ = self.states[state]
        target_code = ord(character)
        low, high = first, first + count - 1
        while low <= high:
            middle = (low + high) // 2
            child, code, _ = self.edges[middle]
            if code == target_code:
                return child
            if code < target_code:
                low = middle + 1
            else:
                high = middle - 1
        return None

    def contains(self, word: str) -> bool:
        state = self.root
        for character in word:
            state = self.transition(state, character)
            if state is None:
                return False
        return self.states[state][2] != 0

    def generate(self, observed: str, broken: frozenset[str], maximum_missing: int) -> list[Candidate]:
        @lru_cache(maxsize=None)
        def search(state: int, observed_index: int, inserted: int) -> tuple[tuple[str, int, int], ...]:
            first, count, terminal_class, _ = self.states[state]
            found: list[tuple[str, int, int]] = []
            if observed_index == len(observed) and inserted > 0 and terminal_class:
                found.append(("", inserted, terminal_class))
            for edge_index in range(first, first + count):
                child, code, _ = self.edges[edge_index]
                character = chr(code)
                if observed_index < len(observed) and character == observed[observed_index]:
                    found.extend(
                        (character + suffix, missing, word_class)
                        for suffix, missing, word_class in search(child, observed_index + 1, inserted)
                    )
                if inserted < maximum_missing and character in broken:
                    found.extend(
                        (character + suffix, missing, word_class)
                        for suffix, missing, word_class in search(child, observed_index, inserted + 1)
                    )
            return tuple(found)

        unique: dict[str, Candidate] = {}
        for word, missing, word_class in search(self.root, 0, 0):
            candidate = Candidate(word, missing, word_class)
            existing = unique.get(word)
            if existing is None or (candidate.missing_count, -candidate.class_id) < (
                existing.missing_count, -existing.class_id
            ):
                unique[word] = candidate
        return [unique[word] for word in sorted(unique)]


def fnv1a64(text: str) -> int:
    value = 14695981039346656037
    for byte in text.encode("utf-8"):
        value ^= byte
        value = (value * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return value


def write_hashed_stats(
    path: Path,
    unigrams: Iterable[tuple[str, int]],
    bigrams: Iterable[tuple[tuple[str, str], int]],
    trigrams: Iterable[tuple[tuple[str, str, str], int]],
) -> dict[str, int]:
    sections = [
        sorted((fnv1a64(word), count) for word, count in unigrams),
        sorted((fnv1a64("\0".join(words)), count) for words, count in bigrams),
        sorted((fnv1a64("\0".join(words)), count) for words, count in trigrams),
    ]
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as target:
        target.write(STATS_MAGIC)
        target.write(struct.pack("<IIII", STATS_VERSION, *(len(section) for section in sections)))
        for section in sections:
            for hashed, count in section:
                target.write(struct.pack("<QI", hashed, min(count, 0xFFFFFFFF)))
    return {
        "unigrams": len(sections[0]),
        "bigrams": len(sections[1]),
        "trigrams": len(sections[2]),
        "bytes": path.stat().st_size,
    }

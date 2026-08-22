#!/usr/bin/env python3
"""Inspect whole-word predictions for hand-written regression cases."""

from __future__ import annotations

import argparse
from pathlib import Path

from evaluate_gap_model import (
    candidate_beam,
    choose_candidate,
    infer_gap_log_probabilities,
    load_model,
)
from train_gap_model import WordExample, load_examples


def main() -> None:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser()
    parser.add_argument("words", nargs="+")
    parser.add_argument("--dataset", type=Path, default=script_dir / "artifacts" / "broken_keys_ru.tsv.gz")
    parser.add_argument("--model", type=Path, default=script_dir / "artifacts" / "broken_key_gap_model.json")
    args = parser.parse_args()

    model, payload = load_model(args.model)
    splits = load_examples(args.dataset)
    all_examples = splits["train"] + splits["validation"] + splits["test"]
    vocabulary = {example.expected for example in all_examples if example.expected == example.observed}
    frequencies: dict[str, int] = {}
    for example in all_examples:
        frequencies[example.expected] = max(frequencies.get(example.expected, 0), example.frequency)
    character_ids = {character: index for index, character in enumerate(payload["characters"])}
    inputs = [WordExample(word.lower(), word.lower(), False, frequencies.get(word.lower(), 0)) for word in args.words]
    gap_predictions = infer_gap_log_probabilities(model, inputs, character_ids, payload["window"], 16384)
    for example, prediction in zip(inputs, gap_predictions):
        candidates = candidate_beam(
            example.observed,
            prediction,
            payload["labels"],
            vocabulary,
            frequencies,
            16,
            2,
        )
        word, score = choose_candidate(candidates, payload["frequency_weight"])
        result = (
            word
            if example.observed not in vocabulary
            and word
            and word != example.observed
            and score >= payload["correction_score_threshold"]
            else example.observed
        )
        print(f"{example.observed} -> {result} (score={score:.3f}, known={example.observed in vocabulary})")


if __name__ == "__main__":
    main()
